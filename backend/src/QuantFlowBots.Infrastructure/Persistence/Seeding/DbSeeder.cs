using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Seeding;

public static class DbSeeder
{
    public static async Task SeedAsync(QuantFlowBotsDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Exchanges.AnyAsync(e => e.Code == "binance", cancellationToken))
        {
            db.Exchanges.Add(new Exchange
            {
                Code = "binance",
                Name = "Binance",
                RestBaseUrl = "https://api.binance.com",
                WebSocketBaseUrl = "wss://stream.binance.com:9443",
                IsActive = true
            });
        }
        if (!await db.Exchanges.AnyAsync(e => e.Code == "binance-futures-testnet", cancellationToken))
        {
            db.Exchanges.Add(new Exchange
            {
                Code = "binance-futures-testnet",
                Name = "Binance Futures (Testnet)",
                RestBaseUrl = "https://testnet.binancefuture.com",
                WebSocketBaseUrl = "wss://stream.binancefuture.com",
                IsActive = true
            });
        }
        if (!await db.Exchanges.AnyAsync(e => e.Code == "binance-spot-testnet", cancellationToken))
        {
            db.Exchanges.Add(new Exchange
            {
                Code = "binance-spot-testnet",
                Name = "Binance Spot (Testnet)",
                RestBaseUrl = "https://testnet.binance.vision",
                WebSocketBaseUrl = "wss://testnet.binance.vision",
                IsActive = true
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureTimescaleAsync(QuantFlowBotsDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS timescaledb;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT create_hypertable('qfb.candles', by_range('OpenTime'), if_not_exists => TRUE, migrate_data => TRUE);",
            cancellationToken);
    }
}
