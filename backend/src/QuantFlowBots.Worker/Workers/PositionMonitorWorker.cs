using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Trading;

namespace QuantFlowBots.Worker.Workers;

public sealed class PositionMonitorWorker(
    PositionMonitor monitor,
    ReconcileService reconcile,
    ITickStreamClient tickClient,
    ITickStreamBus tickBus,
    IOptions<BinanceOptions> binanceOptions,
    ILogger<PositionMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PositionMonitorWorker started.");
        await monitor.LoadOpenPositionsAsync(stoppingToken);
        await reconcile.RunAsync(stoppingToken);

        monitor.SubscriptionChanged += async () =>
        {
            try { await tickClient.UpdateBookTickerSubscriptionsAsync(monitor.WatchedSymbols, stoppingToken); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed updating bookTicker subs"); }
        };

        await tickClient.UpdateBookTickerSubscriptionsAsync(monitor.WatchedSymbols, stoppingToken);
        await tickClient.UpdateAggTradeSubscriptionsAsync(binanceOptions.Value.WatchSymbols, stoppingToken);

        var tickTask = Task.Run(() => tickClient.RunAsync(stoppingToken), stoppingToken);
        var bookTickerTask = Task.Run(async () =>
        {
            await foreach (var evt in tickBus.BookTickers.ReadAllAsync(stoppingToken))
            {
                try { await monitor.OnBookTickerAsync(evt, stoppingToken); }
                catch (Exception ex) { logger.LogError(ex, "Monitor.OnBookTicker failed for {Symbol}", evt.Symbol); }
            }
        }, stoppingToken);

        await Task.WhenAll(tickTask, bookTickerTask);
    }
}
