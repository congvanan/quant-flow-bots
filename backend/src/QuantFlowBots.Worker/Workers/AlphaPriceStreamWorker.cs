using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Infrastructure.Exchanges.Binance;

namespace QuantFlowBots.Worker.Workers;

/// <summary>
/// Polling worker cho realtime price ticker của Alpha × Futures symbols.
///
/// **Vì sao polling thay vì WebSocket**: <c>wss://fstream.binance.com</c> (Futures WS) bị
/// block ở network local — connect handshake thành công nhưng server không gửi message
/// (0 bytes EOF). Verified bằng ws-test.ps1. REST <c>fapi.binance.com</c> thì hoạt động
/// (đã dùng cho klines/exchangeInfo trong <see cref="AlphaMarketService"/>).
///
/// Endpoint <c>GET /fapi/v1/ticker/24hr</c> trả TẤT CẢ futures ticker trong 1 call duy nhất,
/// weight = 40. Poll 3s = 800 weight/phút, thừa thãi trong limit 2400/phút.
///
/// Pipeline:
///   1. Get alpha futures symbol set từ <see cref="AlphaMarketService"/> (refresh mỗi 10 phút)
///   2. GET ticker/24hr → ~600KB JSON
///   3. Lọc theo set Alpha, build <see cref="AlphaPriceTick"/>
///   4. HSET vào Redis qua <see cref="AlphaPriceCache"/>
///
/// Worker giữ tên <c>AlphaPriceStreamWorker</c> để không phá DI registration, dù nay là
/// poller. Class name là implementation detail.
/// </summary>
public sealed class AlphaPriceStreamWorker(
    AlphaMarketService alpha,
    AlphaPriceCache priceCache,
    FuturesPriceCache futuresCache,
    IHttpClientFactory httpFactory,
    ILogger<AlphaPriceStreamWorker> logger) : BackgroundService
{
    private const string TickerUrl = "https://fapi.binance.com/fapi/v1/ticker/24hr";
    private const string PremiumUrl = "https://fapi.binance.com/fapi/v1/premiumIndex";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AlphaPriceStreamWorker started — poll {Sec}s via REST ticker/24hr", PollInterval.TotalSeconds);
        await WaitForSymbolsAsync(stoppingToken);

        var http = httpFactory.CreateClient("alpha-public");
        http.Timeout = TimeSpan.FromSeconds(8);
        long tickCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Set alpha symbols ăn cache 10 phút trong AlphaMarketService → call này ~0 cost.
                var alphaList = await alpha.GetAsync(stoppingToken);
                var alphaSet = alphaList.Select(t => t.FuturesSymbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (alphaSet.Count == 0)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch { return; }
                    continue;
                }

                var written = await PollAndUpdateAsync(http, alphaSet, stoppingToken);
                if (++tickCount == 1 || tickCount % 100 == 0)
                    logger.LogInformation("AlphaPriceStream tick #{N}: {Written} prices upserted (alpha set size={Set})",
                        tickCount, written, alphaSet.Count);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AlphaPriceStream poll failed — retry");
            }

            try { await Task.Delay(PollInterval, stoppingToken); } catch { return; }
        }
    }

    private async Task<int> PollAndUpdateAsync(HttpClient http, HashSet<string> alphaSet, CancellationToken ct)
    {
        // Hai endpoint độc lập: ticker (giá+vol) và premiumIndex (funding). Chạy song song
        // để giảm latency. premiumIndex weight=1 (all symbols) → cost không đáng kể.
        var tickerTask = http.GetAsync(TickerUrl, ct);
        var premiumTask = http.GetAsync(PremiumUrl, ct);
        await Task.WhenAll(tickerTask, premiumTask);

        using var tickerResp = await tickerTask;
        using var premiumResp = await premiumTask;
        tickerResp.EnsureSuccessStatusCode();
        premiumResp.EnsureSuccessStatusCode();

        // Build funding map từ premiumIndex trước, để ticker pass có sẵn data merge.
        var funding = new Dictionary<string, (decimal Rate, DateTimeOffset? NextAt)>(StringComparer.OrdinalIgnoreCase);
        using (var pstream = await premiumResp.Content.ReadAsStreamAsync(ct))
        using (var pdoc = await JsonDocument.ParseAsync(pstream, cancellationToken: ct))
        {
            if (pdoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in pdoc.RootElement.EnumerateArray())
                {
                    var sym = el.TryGetProperty("symbol", out var s) ? s.GetString() : null;
                    if (string.IsNullOrWhiteSpace(sym) || !alphaSet.Contains(sym)) continue;
                    var rate = ReadDec(el, "lastFundingRate");
                    DateTimeOffset? nextAt = null;
                    if (el.TryGetProperty("nextFundingTime", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var ms) && ms > 0)
                        nextAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                    funding[sym!] = (rate, nextAt);
                }
            }
        }

        using var stream = await tickerResp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;

        var now = DateTimeOffset.UtcNow;
        var written = 0;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var sym = el.TryGetProperty("symbol", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(sym)) continue;

            // Populate futures price cache cho MỌI futures symbol (BasisGuard cần phủ rộng
            // hơn alpha set — vd Spot bot trade BTCUSDT vẫn muốn guard basis dù không phải alpha).
            var lastPrice = ReadDec(el, "lastPrice");
            if (lastPrice > 0) futuresCache.Upsert(sym!, lastPrice, now);

            if (!alphaSet.Contains(sym)) continue;

            funding.TryGetValue(sym!, out var f);
            // Field codes: lastPrice, priceChangePercent, highPrice, lowPrice, quoteVolume
            priceCache.Upsert(new AlphaPriceTick(
                Symbol: sym!,
                Price: lastPrice,
                PercentChange24h: ReadDec(el, "priceChangePercent"),
                High24h: ReadDec(el, "highPrice"),
                Low24h: ReadDec(el, "lowPrice"),
                QuoteVolume24h: ReadDec(el, "quoteVolume"),
                FundingRate: f.Rate,
                NextFundingTime: f.NextAt,
                At: now));
            written++;
        }
        return written;
    }

    private async Task WaitForSymbolsAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var list = await alpha.GetAsync(ct);
                if (list.Count > 0) return;
            }
            catch { /* AlphaMarketService tự log */ }
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch { return; }
        }
    }

    private static decimal ReadDec(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0m;
        var s = v.GetString();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
