using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Snapshot 1 token Alpha đã được verify list trên Binance Futures USDT-M.
/// Sparkline = 24 close price liên tiếp (1h × 24 = ~1 ngày), dùng cho mini chart bên cạnh.
/// </summary>
public sealed record AlphaTokenSnapshot(
    string Symbol,
    string FuturesSymbol,
    string Name,
    string? IconUrl,
    string? Chain,
    decimal Price,
    decimal PercentChange24h,
    decimal MarketCap,
    decimal Volume24h,
    decimal? Fdv,
    decimal? Liquidity,
    long? Holders,
    decimal[] Sparkline,
    DateTimeOffset At);

/// <summary>
/// Aggregator cho danh sách Binance Alpha tokens đã list Futures USDT-M.
///
/// Pipeline:
///   1. GET danh sách Alpha (~100-200 token) từ Binance public bapi endpoint
///   2. GET Futures exchangeInfo (production fapi) → set các symbol có sẵn
///   3. Cross-reference: chỉ giữ Alpha token nào có {symbol}USDT tồn tại trên Futures
///   4. Với mỗi token sống sót, GET 24 nến 1h từ Futures klines → sparkline
///
/// Cache TTL = 10 phút (Alpha ranking + price không đổi nhanh, marketCap/volume24h
/// cũng vậy). Tổng cost mỗi refresh: 1 alpha call + 1 exchangeInfo + ~100 klines call
/// (futures weight ~100, trong giới hạn 2400/phút thoải mái).
///
/// SemaphoreSlim gate: lần đọc đầu tiên sau TTL trigger refresh, các caller song song
/// cùng lúc chỉ trả 1 fetch chung. Không cần background worker — endpoint /api/market/alpha
/// gọi GetAsync() là đủ.
/// </summary>
public sealed class AlphaMarketService(IHttpClientFactory httpFactory, ILogger<AlphaMarketService> logger)
{
    private const string AlphaUrl = "https://www.binance.com/bapi/defi/v1/public/wallet-direct/buw/wallet/cex/alpha/all/token/list";
    private const string FuturesBaseUrl = "https://fapi.binance.com";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StaleGrace = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AlphaTokenSnapshot> _cache = [];
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<AlphaTokenSnapshot>> GetAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSuccess < Ttl && _cache.Count > 0) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (now - _lastSuccess < Ttl && _cache.Count > 0) return _cache;

            try
            {
                _cache = await BuildAsync(ct);
                _lastSuccess = DateTimeOffset.UtcNow;
                logger.LogInformation("AlphaMarketService refreshed: {Count} tokens listed on Futures USDT-M", _cache.Count);
                return _cache;
            }
            catch (Exception ex)
            {
                // Serve stale nếu fetch lỗi và còn trong stale grace window (1h).
                if (_cache.Count > 0 && DateTimeOffset.UtcNow - _lastSuccess < StaleGrace)
                {
                    logger.LogWarning(ex, "AlphaMarketService refresh failed — serving stale ({Count} tokens, age={Age}min)",
                        _cache.Count, (int)(DateTimeOffset.UtcNow - _lastSuccess).TotalMinutes);
                    return _cache;
                }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<AlphaTokenSnapshot>> BuildAsync(CancellationToken ct)
    {
        using var http = httpFactory.CreateClient("alpha-public");
        http.Timeout = TimeSpan.FromSeconds(15);

        var alphaTokens = await FetchAlphaTokensAsync(http, ct);
        var futuresSymbols = await FetchFuturesSymbolsAsync(http, ct);
        logger.LogInformation("Alpha pipeline: {Alpha} alpha tokens, {Futures} futures symbols", alphaTokens.Count, futuresSymbols.Count);

        // Cross-reference: chỉ giữ alpha có {symbol}USDT trên futures.
        // Dedupe theo Symbol — Binance Alpha có thể trả nhiều entry cùng symbol khác chain
        // (vd "AIO" trên BSC + "AIO" trên Ethereum đều fork tên), nhưng chỉ có 1 futures
        // AIOUSDT. Giữ entry MarketCap cao nhất để đại diện đúng → tránh row trùng trên UI.
        var listed = alphaTokens
            .Where(t => !string.IsNullOrWhiteSpace(t.Symbol))
            .Where(t => futuresSymbols.Contains(t.Symbol.ToUpperInvariant() + "USDT"))
            .GroupBy(t => t.Symbol.ToUpperInvariant())
            .Select(g => g.OrderByDescending(t => t.MarketCap).First())
            .ToList();

        // Fetch sparkline song song với throttle. 100 call x ~80ms = 8s nếu serial, ~1s nếu // by 10.
        // SemaphoreSlim limit 10 đồng thời cho lành (futures public weight 1/call × 100 = 100, tiêu nhanh nhưng vẫn dưới 2400/phút).
        using var concurrencyGate = new SemaphoreSlim(10, 10);
        var tasks = listed.Select(async t =>
        {
            await concurrencyGate.WaitAsync(ct);
            try
            {
                var futuresSym = t.Symbol.ToUpperInvariant() + "USDT";
                var sparkline = await FetchSparklineAsync(http, futuresSym, ct);
                return new AlphaTokenSnapshot(
                    Symbol: t.Symbol.ToUpperInvariant(),
                    FuturesSymbol: futuresSym,
                    Name: t.Name ?? t.Symbol,
                    IconUrl: t.IconUrl,
                    Chain: t.ChainName,
                    Price: t.Price,
                    PercentChange24h: t.PercentChange24h,
                    MarketCap: t.MarketCap,
                    Volume24h: t.Volume24h,
                    Fdv: t.Fdv,
                    Liquidity: t.Liquidity,
                    Holders: t.Holders,
                    Sparkline: sparkline,
                    At: DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Alpha sparkline fetch failed for {Sym} — emitting without chart", t.Symbol);
                return new AlphaTokenSnapshot(
                    t.Symbol.ToUpperInvariant(), t.Symbol.ToUpperInvariant() + "USDT",
                    t.Name ?? t.Symbol, t.IconUrl, t.ChainName,
                    t.Price, t.PercentChange24h, t.MarketCap, t.Volume24h, t.Fdv, t.Liquidity,
                    t.Holders, [], DateTimeOffset.UtcNow);
            }
            finally { concurrencyGate.Release(); }
        });
        var results = await Task.WhenAll(tasks);
        // Sort theo marketCap desc cho UI có thứ tự ý nghĩa.
        return results.OrderByDescending(t => t.MarketCap).ToList();
    }

    private sealed record AlphaRaw(
        string Symbol, string? Name, string? IconUrl, string? ChainName,
        decimal Price, decimal PercentChange24h, decimal MarketCap, decimal Volume24h,
        decimal? Fdv, decimal? Liquidity, long? Holders);

    private async Task<List<AlphaRaw>> FetchAlphaTokensAsync(HttpClient http, CancellationToken ct)
    {
        // Binance bapi yêu cầu header này; nếu thiếu trả về 451 (geo-block / bot detection).
        using var req = new HttpRequestMessage(HttpMethod.Get, AlphaUrl);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var list = new List<AlphaRaw>(data.GetArrayLength());
        foreach (var el in data.EnumerateArray())
        {
            var sym = el.TryGetProperty("symbol", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(sym)) continue;
            list.Add(new AlphaRaw(
                Symbol: sym!,
                Name: el.TryGetProperty("name", out var n) ? n.GetString() : null,
                IconUrl: el.TryGetProperty("iconUrl", out var ic) ? ic.GetString() : null,
                ChainName: el.TryGetProperty("chainName", out var cn) ? cn.GetString() : null,
                Price: ReadDec(el, "price"),
                PercentChange24h: ReadDec(el, "percentChange24h"),
                MarketCap: ReadDec(el, "marketCap"),
                Volume24h: ReadDec(el, "volume24h"),
                Fdv: TryReadDec(el, "fdv"),
                Liquidity: TryReadDec(el, "liquidity"),
                Holders: TryReadLong(el, "holders")));
        }
        return list;
    }

    private async Task<HashSet<string>> FetchFuturesSymbolsAsync(HttpClient http, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"{FuturesBaseUrl}/fapi/v1/exchangeInfo", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!doc.RootElement.TryGetProperty("symbols", out var arr) || arr.ValueKind != JsonValueKind.Array) return set;
        foreach (var el in arr.EnumerateArray())
        {
            // Chỉ giữ TRADING + USDT margined để khớp universe của user.
            var status = el.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (!string.Equals(status, "TRADING", StringComparison.OrdinalIgnoreCase)) continue;
            var quote = el.TryGetProperty("quoteAsset", out var qa) ? qa.GetString() : null;
            if (!string.Equals(quote, "USDT", StringComparison.OrdinalIgnoreCase)) continue;
            var symbol = el.TryGetProperty("symbol", out var sm) ? sm.GetString() : null;
            if (!string.IsNullOrWhiteSpace(symbol)) set.Add(symbol!);
        }
        return set;
    }

    private static async Task<decimal[]> FetchSparklineAsync(HttpClient http, string futuresSymbol, CancellationToken ct)
    {
        // /fapi/v1/klines: array of [openTime, open, high, low, close, volume, ...] strings.
        var url = $"{FuturesBaseUrl}/fapi/v1/klines?symbol={futuresSymbol}&interval=1h&limit=24";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return [];
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
        var closes = new List<decimal>(doc.RootElement.GetArrayLength());
        foreach (var k in doc.RootElement.EnumerateArray())
        {
            if (k.ValueKind != JsonValueKind.Array || k.GetArrayLength() < 5) continue;
            var closeStr = k[4].GetString();
            if (decimal.TryParse(closeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) closes.Add(v);
        }
        return closes.ToArray();
    }

    private static decimal ReadDec(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0m;
        return v.ValueKind switch
        {
            JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m,
            JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : 0m,
            _ => 0m,
        };
    }

    private static decimal? TryReadDec(JsonElement el, string prop)
    {
        var d = ReadDec(el, prop);
        return d == 0m ? null : d;
    }

    private static long? TryReadLong(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : null,
            JsonValueKind.String => long.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n2) ? n2 : null,
            _ => null,
        };
    }
}
