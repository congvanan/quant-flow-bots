using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Exchanges.Binance;

namespace QuantFlowBots.Infrastructure.Trading;

/// <summary>
/// Catalog filter (đồng bộ với FE <c>CONTEXT_FILTER_OPTIONS</c>):
///   <list type="bullet">
///     <item>spotTrend     — spot 24h % cùng chiều entry side</item>
///     <item>btcDominance  — BTC.D không tăng (stub: chưa có data source)</item>
///     <item>whaleAlert    — không có whale alert ngược chiều (stub)</item>
///     <item>wallAlert     — không có wall lớn chắn entry</item>
///     <item>sentiment     — sentiment không tiêu cực (stub: cần SentimentEvents query)</item>
///     <item>fundingRate   — |funding rate| ≤ FundingRateThreshold</item>
///   </list>
/// Filter chưa có data → trả Unknown (fail-open). Khi infra sẵn sàng (Phase 3.1+),
/// đổi stub thành read thật mà không phá API.
/// </summary>
public sealed class ContextFilterRunner(
    TickerSnapshotCache spotTicker,
    AlphaPriceCache alphaCache,
    OrderBookWallCache wallCache,
    ILogger<ContextFilterRunner> logger) : IContextFilterRunner
{
    // Funding rate "trong ngưỡng": |rate| ≤ 0.1%/kỳ (8h). Vượt → market crowded/nóng.
    private const decimal FundingRateThreshold = 0.001m;
    // Wall coi là "chắn entry" nếu nằm trong 0.5% từ mid và cùng phía chống đối entry.
    private const decimal WallDistanceThresholdPct = 0.5m;

    public async Task<ContextFilterResult> CheckAsync(
        string? contextFiltersJson, string symbol, OrderSide side, CancellationToken ct)
    {
        var filters = ParseFilters(contextFiltersJson);
        if (filters.Count == 0) return ContextFilterResult.Pass;

        foreach (var key in filters)
        {
            var r = key switch
            {
                "spotTrend"    => await CheckSpotTrendAsync(symbol, side, ct),
                "fundingRate"  => CheckFundingRate(symbol),
                "wallAlert"    => CheckWallAlert(symbol, side),
                // Stubs — fail-open (Unknown). Phase 3.1+ wire dần khi có data source.
                "btcDominance" => null,
                "whaleAlert"   => null,
                "sentiment"    => null,
                _ => null,
            };
            if (r is { Ok: false }) return r;
        }
        return ContextFilterResult.Pass;
    }

    private static List<string> ParseFilters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("filters", out var arr) ||
                arr.ValueKind != JsonValueKind.Array) return [];
            var result = new List<string>(arr.GetArrayLength());
            foreach (var el in arr.EnumerateArray())
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s) result.Add(s);
            return result;
        }
        catch { return []; }
    }

    private async Task<ContextFilterResult?> CheckSpotTrendAsync(string symbol, OrderSide side, CancellationToken ct)
    {
        var tickers = await spotTicker.GetAsync(ct);
        var spot = tickers.FirstOrDefault(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (spot is null) return null; // unknown → fail-open

        // Buy entry cần trend dương; Sell cần trend âm. Equal cho 0% qua (không khoá flat market).
        var pct = spot.PriceChangePercent;
        return side switch
        {
            OrderSide.Buy when pct < 0
                => ContextFilterResult.Block("spotTrend", $"spot 24h pct={pct:F2}% < 0 (ngược chiều Buy)"),
            OrderSide.Sell when pct > 0
                => ContextFilterResult.Block("spotTrend", $"spot 24h pct={pct:F2}% > 0 (ngược chiều Sell)"),
            _ => null,
        };
    }

    private ContextFilterResult? CheckFundingRate(string symbol)
    {
        // AlphaPriceCache có funding cho ~216 alpha symbols. Symbol khác → Unknown.
        var tick = alphaCache.Snapshot().FirstOrDefault(t =>
            string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (tick is null) return null;

        var abs = Math.Abs(tick.FundingRate);
        return abs > FundingRateThreshold
            ? ContextFilterResult.Block("fundingRate", $"|funding|={abs * 100:F4}% > {FundingRateThreshold * 100:F4}%")
            : null;
    }

    private ContextFilterResult? CheckWallAlert(string symbol, OrderSide side)
    {
        // Buy chống bị SELL wall (ask wall) chắn phía trên; Sell chống BUY wall (bid wall) chắn dưới.
        // Wall "chắn" = cùng phía đối kháng + cách mid ≤ WallDistanceThresholdPct.
        var hostileSide = side == OrderSide.Buy ? "SELL" : "BUY";
        var walls = wallCache.Snapshot();
        var hostile = walls.FirstOrDefault(w =>
            string.Equals(w.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(w.Side, hostileSide, StringComparison.OrdinalIgnoreCase) &&
            w.DistanceFromMidPercent <= WallDistanceThresholdPct);
        if (hostile is null) return null;

        return ContextFilterResult.Block("wallAlert",
            $"{hostileSide} wall {hostile.QuoteNotional:N0} USDT @ {hostile.Price} ({hostile.DistanceFromMidPercent:F3}% from mid)");
    }
}
