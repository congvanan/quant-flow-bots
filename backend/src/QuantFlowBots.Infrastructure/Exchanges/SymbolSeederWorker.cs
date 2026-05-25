using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Exchanges;

public sealed class SymbolSeederWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SymbolSeederWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SymbolSeeder failed.");
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        var exchange = await db.Exchanges.FirstOrDefaultAsync(e => e.Code == "binance", cancellationToken);
        if (exchange is null)
        {
            logger.LogWarning("SymbolSeeder: binance exchange row missing, skipping.");
            return;
        }

        var client = scope.ServiceProvider.GetRequiredService<IExchangeClient>();
        logger.LogInformation("SymbolSeeder: fetching exchangeInfo from Binance...");
        var infos = await client.GetSymbolsAsync(cancellationToken);
        logger.LogInformation("SymbolSeeder: fetched {Count} symbols, upserting...", infos.Count);

        var existing = await db.Symbols
            .Where(s => s.ExchangeId == exchange.Id)
            .ToDictionaryAsync(s => s.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var added = 0;
        var refreshed = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var info in infos)
        {
            if (existing.TryGetValue(info.Code, out var row))
            {
                // Refresh filters if they look stale or out of date
                if (row.MinQuantity != info.MinQuantity
                    || row.TickSize != info.TickSize
                    || row.StepSize != info.StepSize
                    || row.MinNotional != info.MinNotional)
                {
                    row.MinQuantity = info.MinQuantity;
                    row.TickSize = info.TickSize;
                    row.StepSize = info.StepSize;
                    row.MinNotional = info.MinNotional;
                    row.FiltersUpdatedAt = now;
                    refreshed++;
                }
                continue;
            }
            var listed = await client.GetSymbolListingDateAsync(info.Code, cancellationToken);
            db.Symbols.Add(new Symbol
            {
                ExchangeId = exchange.Id,
                Code = info.Code,
                BaseAsset = info.BaseAsset,
                QuoteAsset = info.QuoteAsset,
                MinQuantity = info.MinQuantity,
                TickSize = info.TickSize,
                StepSize = info.StepSize,
                MinNotional = info.MinNotional,
                FiltersUpdatedAt = now,
                IsActive = true,
                ListedAt = listed,
            });
            added++;
            await Task.Delay(200, cancellationToken);
        }
        if (added > 0 || refreshed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        logger.LogInformation("SymbolSeeder: inserted {Added}, refreshed filters on {Refreshed} symbols.", added, refreshed);
    }
}
