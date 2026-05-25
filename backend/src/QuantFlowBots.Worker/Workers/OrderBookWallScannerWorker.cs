using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Trading;

namespace QuantFlowBots.Worker.Workers;

/// <summary>
/// Periodically fetch /api/v3/depth for the top-N USDT pairs by 24h quote volume,
/// flag any single price level whose notional (price*qty) ≥ MinNotionalUsdt and
/// lies within MaxDistanceFromMidPercent of mid. Each detection is upserted into
/// <see cref="OrderBookWallCache"/> (so /api/market/order-book-walls reads fresh)
/// and published on <see cref="IOrderBookWallBus"/> for SignalR fan-out.
/// </summary>
public sealed class OrderBookWallScannerWorker(
    IServiceScopeFactory scopeFactory,
    OrderBookWallCache cache,
    IOrderBookWallBus bus,
    IBinanceGate gate,
    IOptions<OrderBookWallOptions> options,
    ILogger<OrderBookWallScannerWorker> logger) : BackgroundService
{
    private static readonly Random _rng = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        var baseInterval = TimeSpan.FromSeconds(Math.Max(15, opt.ScanIntervalSeconds));
        logger.LogInformation("OrderBookWallScannerWorker started interval={Sec}s detectFloor={Floor:N0}USDT defaultFilter={Default:N0}USDT maxDist={Dist}%",
            baseInterval.TotalSeconds, opt.DetectionFloorUsdt, opt.MinNotionalUsdt, opt.MaxDistanceFromMidPercent);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Yield when Binance gate is open — avoid hammering during ban + skip thundering herd
            // when it closes. Use the cooldown deadline so we wake up right after it ends.
            var state = await gate.GetStateAsync(stoppingToken);
            if (state.IsOpen)
            {
                var wait = state.Until!.Value - DateTimeOffset.UtcNow + Jitter(TimeSpan.FromSeconds(5));
                if (wait < TimeSpan.FromSeconds(10)) wait = TimeSpan.FromSeconds(10);
                logger.LogWarning("OrderBookWall: gate OPEN, sleeping {Wait}s until {Until}", (int)wait.TotalSeconds, state.Until);
                try { await Task.Delay(wait, stoppingToken); } catch { return; }
                continue;
            }

            try { await TickAsync(opt, stoppingToken); }
            catch (BinanceGateOpenException) { /* gate just tripped mid-scan; loop will yield */ }
            catch (Exception ex) { logger.LogError(ex, "OrderBookWall scan failed"); }

            // ±20% jitter on the normal interval to break sync with other workers.
            var jittered = baseInterval + Jitter(baseInterval * 0.2);
            try { await Task.Delay(jittered, stoppingToken); } catch { return; }
        }
    }

    private static TimeSpan Jitter(TimeSpan span) =>
        TimeSpan.FromMilliseconds((_rng.NextDouble() * 2 - 1) * span.TotalMilliseconds);

    private async Task TickAsync(OrderBookWallOptions opt, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var binance = scope.ServiceProvider.GetRequiredService<BinanceRestClient>();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

        var tickers = await binance.GetAllTickersAsync(cancellationToken);
        var quoteFilter = opt.QuoteAssets is { Length: > 0 } ? opt.QuoteAssets : ["USDT"];
        var excludedBases = opt.ExcludedBaseAssets
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eligibleSymbols = await db.Symbols
            .Where(s => s.IsActive)
            .Where(s => quoteFilter.Contains(s.QuoteAsset))
            .Where(s => !excludedBases.Contains(s.BaseAsset))
            .Select(s => s.Code)
            .ToListAsync(cancellationToken);
        var eligibleSet = eligibleSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var watch = tickers
            .Where(t => eligibleSet.Contains(t.Symbol))
            .OrderByDescending(t => t.QuoteVolume)
            .Take(Math.Clamp(opt.MaxSymbols, 1, 200))
            .Select(t => t.Symbol)
            .ToList();

        var found = 0;
        foreach (var symbol in watch)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var snap = await binance.GetDepthAsync(symbol, opt.DepthLimit, cancellationToken);
            if (snap is null || snap.Bids.Length == 0 || snap.Asks.Length == 0) continue;

            var bestBid = snap.Bids[0].Price;
            var bestAsk = snap.Asks[0].Price;
            var mid = (bestBid + bestAsk) / 2m;
            if (mid <= 0m) continue;

            // Skip dollar-pegged pairs (USDC/USDT, RLUSD/USDT, UUSDT, ...). Their walls are
            // peg-defense liquidity, not market signal. Dynamic check catches future stables
            // by name without code changes.
            if (opt.StablePairPriceBandPercent > 0m &&
                Math.Abs(mid - 1m) / 1m * 100m <= opt.StablePairPriceBandPercent) continue;

            var avgBidNotional = AvgNotional(snap.Bids);
            var avgAskNotional = AvgNotional(snap.Asks);

            await ScanSideAsync(symbol, "Bid", snap.Bids, mid, avgBidNotional, opt, found_ => found += found_, cancellationToken);
            await ScanSideAsync(symbol, "Ask", snap.Asks, mid, avgAskNotional, opt, found_ => found += found_, cancellationToken);
        }

        logger.LogInformation("Wall scan: {Symbols} symbols → {Found} walls ≥ {Floor:N0} USDT (cached)", watch.Count, found, opt.DetectionFloorUsdt);
    }

    private async Task ScanSideAsync(
        string symbol,
        string side,
        (decimal Price, decimal Qty)[] levels,
        decimal mid,
        decimal avgNotional,
        OrderBookWallOptions opt,
        Action<int> incFound,
        CancellationToken cancellationToken)
    {
        foreach (var lvl in levels)
        {
            var notional = lvl.Price * lvl.Qty;
            if (notional < opt.DetectionFloorUsdt) continue;
            var distPct = Math.Abs(lvl.Price - mid) / mid * 100m;
            if (distPct > opt.MaxDistanceFromMidPercent) continue;
            var multiplier = avgNotional > 0m ? notional / avgNotional : 0m;
            var evt = new OrderBookWallEvent(
                symbol, side, lvl.Price, lvl.Qty, notional, mid, distPct, multiplier, DateTimeOffset.UtcNow);
            cache.Upsert(evt);
            await bus.PublishAsync(evt, cancellationToken);
            incFound(1);
        }
    }

    private static decimal AvgNotional((decimal Price, decimal Qty)[] levels)
    {
        if (levels.Length == 0) return 0m;
        decimal sum = 0m;
        var n = Math.Min(levels.Length, 20);
        for (var i = 0; i < n; i++) sum += levels[i].Price * levels[i].Qty;
        return sum / n;
    }
}
