using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Worker.Workers;

public sealed class MarketStreamWorker(
    IMarketStreamClient stream,
    IOptions<BinanceOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<MarketStreamWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var primary = options.Value.WatchSymbols;
        logger.LogInformation("MarketStreamWorker subscribing primary tickers + klines: {Count} symbols.", primary.Length);
        await stream.SubscribeTickersAsync(primary, stoppingToken);
        await stream.SubscribeKlinesAsync(primary, CandleInterval.OneMinute, stoppingToken);

        var usdtSymbols = await LoadUsdtSymbolsAsync(stoppingToken);
        if (usdtSymbols.Count > 0)
        {
            logger.LogInformation("MarketStreamWorker subscribing kline_1m for {Count} USDT pairs (for volume-spike detection).", usdtSymbols.Count);
            await stream.SubscribeKlinesAsync(usdtSymbols, CandleInterval.OneMinute, stoppingToken);
        }

        await stream.RunAsync(stoppingToken);
    }

    private async Task<IReadOnlyList<string>> LoadUsdtSymbolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
            return await db.Symbols
                .Where(s => s.IsActive && s.QuoteAsset == "USDT")
                .OrderBy(s => s.Code)
                .Select(s => s.Code)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MarketStreamWorker failed loading USDT symbols, continuing with primary only.");
            return [];
        }
    }
}
