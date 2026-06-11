using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Risk;
using QuantFlowBots.Application.Sentiment;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Worker.Workers;

/// <summary>
/// Pulls https://t.me/s/binance_announcements every 5 min (public preview HTML, no token needed),
/// extracts new messages since last scan, ingests them as sentiment events (auto-scored by the
/// existing KeywordSentimentScorer), and — when a message contains red-flag keywords like
/// "delist", "hack", "suspend trading" — blocks the mentioned symbols in SymbolRiskGate.
///
/// Blocking a symbol additionally triggers an auto-close pass: any open position on that symbol
/// gets a market sell via TradingDispatcher. User chose this in Đợt I follow-up — delist usually
/// = liquidity vanishes within hours, so flat-NOW beats getting stuck.
/// </summary>
public sealed partial class BinanceAnnouncementWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    SymbolRiskGate riskGate,
    ILogger<BinanceAnnouncementWorker> logger) : BackgroundService
{
    // 2-min poll keeps lag from announcement → block ≤ 2min. t.me preview is HTML — no documented
    // rate limit but pulling more aggressively risks IP throttling; 2min is the empirical sweet spot.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private const string TelegramUrl = "https://t.me/s/binance_announcements";
    private const string Source = "binance_announcement";

    // Red flag = bot must NOT trade this symbol. Tighter than initial draft because Binance
    // often posts "Notice of Removal of Margin Trading" which only removes MARGIN — spot still
    // works fine. We require either an explicit "delist" verb, or "remove" co-located with
    // "spot" / "trading pair", or unambiguous security wording.
    private static readonly string[] RedFlagKeywords =
    [
        "delist", "delisting", "will delist",
        "hack", "exploit", "security incident", "stolen funds", "compromise",
    ];

    /// <summary>
    /// True for "Binance Will Delist X, Y, Z" full-token delistings and security incidents.
    /// We intentionally EXCLUDE "Notice of Removal of Spot Trading Pairs" — that only removes
    /// specific pairs (e.g. X/BTC) while leaving X/USDT trading, so it's not a risk to USDT bots.
    /// </summary>
    private static bool IsRedFlag(string text)
    {
        var lower = text.ToLowerInvariant();

        // Hard exclusions — anything mentioning these is NOT a full-token delist.
        if (lower.Contains("margin trading")) return false;
        if (lower.Contains("isolated margin")) return false;
        if (lower.Contains("cross margin")) return false;
        if (lower.Contains("leveraged token")) return false;
        if (lower.Contains("convert quote")) return false;
        if (lower.Contains("trading bots service")) return false;
        if (lower.Contains("removal of spot trading pair")) return false;   // pair-only removal
        if (lower.Contains("removal of spot trading pairs")) return false;

        // Hard inclusions — explicit delist verb or security wording.
        return RedFlagKeywords.Any(k => lower.Contains(k));
    }

    private DateTimeOffset _lastSeenAt = DateTimeOffset.MinValue;
    private bool _purgedThisRun;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BinanceAnnouncementWorker started ({Poll}min).", PollInterval.TotalMinutes);
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);   // let DB settle

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogWarning(ex, "BinanceAnnouncementWorker poll failed"); }
            try { await Task.Delay(PollInterval, stoppingToken); } catch { return; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 QuantFlowBots/1.0");

        string html;
        try { html = await http.GetStringAsync(TelegramUrl, ct); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch Telegram preview");
            return;
        }

        var messages = ParseMessages(html);
        if (messages.Count == 0) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

        // One-time guard: purge dupes hiện hữu (Worker từng re-process khi restart vì _lastSeenAt
        // chỉ in-memory). Idempotent + chạy 1 lần đầu poll mỗi worker run → cost rẻ.
        if (!_purgedThisRun)
        {
            await PurgeDuplicateSentimentAsync(db, ct);
            _purgedThisRun = true;
        }
        var scorer = scope.ServiceProvider.GetRequiredService<ISentimentScorer>();
        var agg = scope.ServiceProvider.GetRequiredService<ISentimentAggregator>();
        var bus = scope.ServiceProvider.GetRequiredService<ISentimentBus>();

        // Known USDT symbols — used both for headline → symbol resolution and to skip
        // mentioning random tickers that aren't tradeable here.
        var symbolByBase = await db.Symbols
            .Where(s => s.QuoteAsset == "USDT")
            .Select(s => new { s.Code, s.BaseAsset })
            .ToDictionaryAsync(s => s.BaseAsset.ToUpperInvariant(), s => s.Code, ct);

        var maxSeenAt = _lastSeenAt;
        foreach (var msg in messages.OrderBy(m => m.At))
        {
            if (msg.At <= _lastSeenAt) continue;
            if (msg.At > maxSeenAt) maxSeenAt = msg.At;

            // Only process true delist / security events. Everything else (listings, AMAs,
            // partnership posts, pair-only removals, margin notices) is intentionally dropped —
            // they pollute the sentiment feed and don't drive any auto-close decision.
            if (!IsRedFlag(msg.Text)) continue;

            var matchedSymbols = ExtractSymbols(msg.Text, symbolByBase);
            if (matchedSymbols.Count == 0) continue;   // no resolvable symbol → nothing to do
            var isRedFlag = true;
            var targets = matchedSymbols;
            foreach (var symbol in targets)
            {
                // Dedup: nếu (Source, Url, SymbolCode) đã có row → skip insert mà vẫn cập nhật
                // EWMA + bus (để aggregator không bỏ sót khi process pull lại). Cost 1 query/symbol;
                // 2-min poll cadence + ~4 symbols/message → không đáng kể.
                var existed = !string.IsNullOrEmpty(msg.Url) && await db.SentimentEvents
                    .AnyAsync(e => e.Source == Source && e.Url == msg.Url && e.SymbolCode == symbol, ct);

                var scored = scorer.Score(new SentimentInput(symbol, Source, msg.Text, msg.Url, msg.At, Tags: "binance"));
                agg.Apply(scored);
                if (existed)
                {
                    // Skip insert nhưng vẫn process risk gate (idempotent — BlockAsync sẽ no-op nếu
                    // đã block) để khôi phục state đúng khi Worker restart sau crash dài.
                    if (isRedFlag && symbol != "MARKET")
                        await riskGate.BlockAsync(symbol, RedFlagReason(msg.Text), Source, msg.Url, ct);
                    continue;
                }
                var symbolId = symbol == "MARKET" ? (int?)null
                    : await db.Symbols.Where(s => s.Code == symbol).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
                db.SentimentEvents.Add(new SentimentEvent
                {
                    SymbolCode = symbol, SymbolId = symbolId,
                    Source = Source,
                    Headline = scored.Headline.Length > 512 ? scored.Headline[..512] : scored.Headline,
                    Url = scored.Url,
                    Score = scored.Score, Magnitude = scored.Magnitude,
                    Tags = scored.Tags,
                    At = scored.At, IngestedAt = DateTimeOffset.UtcNow,
                });
                await bus.PublishAsync(scored, ct);

                if (isRedFlag && symbol != "MARKET")
                {
                    var newlyBlocked = !riskGate.IsBlocked(symbol);
                    await riskGate.BlockAsync(symbol, RedFlagReason(msg.Text), Source, msg.Url, ct);
                    if (newlyBlocked)
                    {
                        logger.LogWarning("RED FLAG on {Symbol}: {Headline}", symbol, msg.Text);
                        await AutoCloseOpenPositionsAsync(scope, symbol, ct);
                    }
                }
            }
        }
        await db.SaveChangesAsync(ct);
        _lastSeenAt = maxSeenAt;
    }

    /// <summary>
    /// Xóa bản sao SentimentEvent từ cùng 1 announcement Binance (cùng Source+Url+SymbolCode)
    /// còn lại 1 row (oldest = IngestedAt sớm nhất). Lý do: Worker restart reset _lastSeenAt
    /// → re-process cùng message nhiều lần → mỗi lần insert N rows trùng. FE group-by-url
    /// hiển thị badge trùng cho user.
    ///
    /// Raw SQL vì EF Core LINQ không express NOT IN với multi-column key gọn được; cost ~1ms
    /// trên ~hundreds rows. Chạy 1 lần đầu poll mỗi worker run (idempotent).
    /// </summary>
    private static async Task PurgeDuplicateSentimentAsync(QuantFlowBotsDbContext db, CancellationToken ct)
    {
        // Postgres: ROW_NUMBER window — keep oldest row mỗi (Source, Url, SymbolCode), xóa rest.
        // (Id là uuid → không dùng MIN(Id) được; chọn keeper theo IngestedAt sớm nhất, tie-break Id.)
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM qfb.sentiment_events
            WHERE ""Id"" IN (
                SELECT ""Id"" FROM (
                    SELECT ""Id"",
                           ROW_NUMBER() OVER (
                               PARTITION BY ""Source"", ""Url"", ""SymbolCode""
                               ORDER BY ""IngestedAt"", ""Id""
                           ) AS rn
                    FROM qfb.sentiment_events
                    WHERE ""Source"" = 'binance_announcement' AND ""Url"" IS NOT NULL
                ) ranked
                WHERE rn > 1
            );", ct);
    }

    /// <summary>Find any open position on the symbol and market-close it via the dispatcher.</summary>
    private async Task AutoCloseOpenPositionsAsync(IServiceScope scope, string symbol, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<Application.Trading.ITradingDispatcher>();

        var open = await db.Positions
            .Where(p => p.Status == PositionStatus.Open && p.Symbol!.Code == symbol)
            .Select(p => new { p.Id, p.BotId, p.SymbolId, p.Quantity, p.EntryPrice })
            .ToListAsync(ct);

        foreach (var p in open)
        {
            try
            {
                // Use entry price as a fallback execution reference; paper-mode mark fills at this
                // price, live executors override with current bid/ask inside the dispatcher chain.
                await dispatcher.ExecuteAsync(new Application.Trading.PaperOrderRequest(
                    p.BotId, null, p.SymbolId, OrderSide.Sell, p.Quantity, p.EntryPrice, "auto:risk_block"), ct);
                logger.LogWarning("Auto-closed position {Pos} on {Symbol} due to risk flag", p.Id, symbol);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-close failed for {Pos} on {Symbol}", p.Id, symbol);
            }
        }
    }

    private static string RedFlagReason(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("hack") || lower.Contains("exploit") || lower.Contains("stolen")) return "security_incident";
        if (lower.Contains("suspend")) return "trading_suspension";
        if (lower.Contains("delist") || lower.Contains("remove")) return "delisting_announced";
        return "binance_alert";
    }

    private static List<string> ExtractSymbols(string text, IReadOnlyDictionary<string, string> baseAssetToCode)
    {
        // Two precise patterns — anything else is too noisy (we used to nab 'BTC' from 'X/BTC',
        // wrongly flagging BTCUSDT). False-positive cost is huge: bot auto-closes a real position.
        var found = new HashSet<string>();

        // Pattern A — explicit X/USDT pair. Only this is unambiguous evidence that X's USDT pair
        // is the target. Pairs against other quotes (X/BTC, X/ETH) intentionally do NOT flag XUSDT
        // because Binance often removes only some quote pairs while keeping USDT.
        foreach (Match m in PairUsdtRegex().Matches(text))
        {
            var baseAsset = m.Groups["base"].Value.ToUpperInvariant();
            if (baseAssetToCode.TryGetValue(baseAsset, out var code))
                found.Add(code);
        }

        // Pattern B — full coin delist phrasing: "Binance Will Delist X, Y, Z on YYYY-MM-DD".
        // Trước đây dùng BareTickerRegex {2,10} → ticker 1 ký tự (vd "D" → DUSDT) bị BỎ QUA.
        // Sửa: explicit list parse giữa "delist" và terminator ("on", " at ", end), split theo
        // dấu phẩy/" and ". Mỗi segment trim + uppercase → resolve qua baseAssetToCode.
        // Cách này vừa bắt được ticker 1 ký tự, vừa tránh false positive từ bare-regex matching
        // các từ in hoa ngẫu nhiên trong preamble.
        var lower = text.ToLowerInvariant();
        var delistIdx = lower.IndexOf("delist", StringComparison.Ordinal);
        if (delistIdx >= 0)
        {
            // Slice sau "delist " — bỏ qua "delist" + space để khỏi nuốt vào segment đầu.
            var sliceStart = delistIdx + "delist".Length;
            // Terminator phổ biến: "on YYYY-MM-DD" (date), "at HH:MM" (time), "from" (action). Cắt tại đó.
            var slice = text[sliceStart..];
            var sliceLower = slice.ToLowerInvariant();
            foreach (var stop in (string[])[" on ", " at ", " from ", "\n", "."])
            {
                var stopIdx = sliceLower.IndexOf(stop, StringComparison.Ordinal);
                if (stopIdx > 0) { slice = slice[..stopIdx]; break; }
            }
            // Split theo "," và " and " (ASCII boundary an toàn — message Binance dùng tiếng Anh).
            var segments = slice
                .Replace(" and ", ",", StringComparison.OrdinalIgnoreCase)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var seg in segments)
            {
                // Token = chữ A-Z 1-10 ký tự (tăng từ {2,10}; "D" hợp lệ). Tránh khớp cả câu.
                var match = SingleTickerRegex().Match(seg);
                if (!match.Success) continue;
                var token = match.Value.ToUpperInvariant();
                if (baseAssetToCode.TryGetValue(token, out var code))
                    found.Add(code);
            }
        }
        return found.ToList();
    }

    /// <summary>Parses Telegram's t.me/s/&lt;channel&gt; HTML into messages. Cheap regex over the
    /// `tgme_widget_message` blocks — keeps us off heavier HTML libs since the markup is stable.</summary>
    private static List<TelegramMessage> ParseMessages(string html)
    {
        var list = new List<TelegramMessage>();
        foreach (Match m in MessageRegex().Matches(html))
        {
            var dateStr = m.Groups["dt"].Value;
            var url = m.Groups["url"].Value;
            var rawText = m.Groups["text"].Value;
            if (!DateTimeOffset.TryParse(dateStr, out var at)) continue;
            var clean = StripHtml(rawText).Trim();
            if (clean.Length < 8) continue;
            list.Add(new TelegramMessage(at, clean, url));
        }
        return list;
    }

    private static string StripHtml(string s) => HtmlTagRegex().Replace(s, " ").Replace("&nbsp;", " ").Trim();

    [GeneratedRegex(@"\b(?<base>[A-Z0-9]{2,10})/USDT\b")] private static partial Regex PairUsdtRegex();
    [GeneratedRegex(@"\b[A-Z]{2,10}\b")] private static partial Regex BareTickerRegex();
    // Match 1 token A-Z trong segment đã split — cho phép 1-10 ký tự (DUSDT cần "D" hợp lệ).
    [GeneratedRegex(@"^\s*([A-Z]{1,10})\s*$")] private static partial Regex SingleTickerRegex();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex HtmlTagRegex();
    // Pulls each post: the datetime is on a <time datetime="..."> inside the meta, and the
    // post URL is on <a class="tgme_widget_message_date" href="...">; the body is in
    // <div class="tgme_widget_message_text" ...>...</div>.
    [GeneratedRegex(@"tgme_widget_message_date""\s+href=""(?<url>[^""]+)"".*?datetime=""(?<dt>[^""]+)"".*?tgme_widget_message_text[^>]*>(?<text>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex MessageRegex();

    private sealed record TelegramMessage(DateTimeOffset At, string Text, string Url);
}
