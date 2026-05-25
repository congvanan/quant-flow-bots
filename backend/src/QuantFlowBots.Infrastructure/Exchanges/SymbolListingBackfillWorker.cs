using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Exchanges;

public sealed class SymbolListingBackfillWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SymbolListingBackfillWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var done = await RunBatchAsync(stoppingToken);
                if (done == 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SymbolListingBackfill batch failed");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<IExchangeClient>();

        var pending = await db.Symbols
            .Where(s => s.ListedAt == null && s.IsActive)
            .OrderByDescending(s => s.QuoteAsset == "USDT")
            .ThenByDescending(s => s.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return 0;

        logger.LogInformation("SymbolListingBackfill: batch {Count} symbols.", pending.Count);
        var updated = 0;
        foreach (var sym in pending)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var listed = await client.GetSymbolListingDateAsync(sym.Code, cancellationToken);
            if (listed.HasValue)
            {
                sym.ListedAt = listed;
                updated++;
            }
            else
            {
                sym.ListedAt = DateTimeOffset.UnixEpoch;
            }
            await Task.Delay(Throttle, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("SymbolListingBackfill: persisted {Updated}/{Total}.", updated, pending.Count);
        return pending.Count;
    }
}
