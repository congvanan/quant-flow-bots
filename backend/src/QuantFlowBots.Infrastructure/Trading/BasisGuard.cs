using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Infrastructure.Exchanges.Binance;

namespace QuantFlowBots.Infrastructure.Trading;

/// <summary>
/// Implementation của <see cref="IBasisGuard"/>.
///
/// Sources:
///   - Spot: <see cref="TickerSnapshotCache"/> (24h ticker, refresh ~10s).
///   - Futures: <see cref="FuturesPriceCache"/> (Redis HASH "futures:prices",
///     refresh 3s từ <c>AlphaPriceStreamWorker</c>).
///
/// **Fail-open policy**: nếu thiếu spot hoặc futures price → trả <see cref="BasisCheckResult.Unknown"/>
/// (Ok=true) chứ không block. Trade-off: bỏ qua guard còn hơn khóa bot khi cache cold sau restart.
/// Log warning để dễ trace nếu xảy ra liên tục.
///
/// **Staleness**: nếu futures tick > 30s cũ, coi như Unknown. Spot ticker cache có grace 2 phút
/// đã handle staleness ở chính cache layer.
/// </summary>
public sealed class BasisGuard(
    TickerSnapshotCache spotTicker,
    FuturesPriceCache futuresPrices,
    ILogger<BasisGuard> logger) : IBasisGuard
{
    private static readonly TimeSpan FuturesMaxAge = TimeSpan.FromSeconds(30);

    public async Task<BasisCheckResult> CheckAsync(string symbol, decimal maxBasisPct, CancellationToken ct)
    {
        if (maxBasisPct <= 0m) return BasisCheckResult.Unknown("guard_disabled");

        var futTick = futuresPrices.TryGet(symbol);
        if (futTick is null)
        {
            logger.LogDebug("BasisGuard {Symbol}: futures price unavailable", symbol);
            return BasisCheckResult.Unknown("futures_price_unavailable");
        }
        if (DateTimeOffset.UtcNow - futTick.Value.At > FuturesMaxAge)
            return BasisCheckResult.Unknown("futures_price_stale");

        var tickers = await spotTicker.GetAsync(ct);
        var spot = tickers.FirstOrDefault(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (spot is null || spot.LastPrice <= 0m)
            return BasisCheckResult.Unknown("spot_price_unavailable");

        var futPrice = futTick.Value.Price;
        // Quy ước: basis% = |spot − futures| / spot × 100. Dùng spot làm denominator để align
        // với cách user hay đọc ("futures lệch X% so với spot").
        var basisPct = Math.Abs(spot.LastPrice - futPrice) / spot.LastPrice * 100m;
        if (basisPct > maxBasisPct)
            return BasisCheckResult.Block(basisPct,
                $"basis_exceeds_limit: |spot {spot.LastPrice} − fut {futPrice}| / spot = {basisPct:F4}% > {maxBasisPct:F4}%");

        return BasisCheckResult.Pass(basisPct);
    }
}
