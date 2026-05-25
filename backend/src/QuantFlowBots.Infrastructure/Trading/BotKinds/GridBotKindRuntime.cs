using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading.BotKinds;

/// <summary>
/// Spot grid: precompute N equally-spaced price rungs between Lower and Upper.
/// On candle close: BUY when price crosses a rung downward (from above to below),
/// SELL the same quantity when price crosses next-higher rung upward.
/// Last-price per bot is cached in-memory; on cold start uses current close as anchor.
/// </summary>
public sealed class GridBotKindRuntime(
    IServiceScopeFactory scopeFactory,
    IBotEventBus botBus,
    ILogger<GridBotKindRuntime> logger) : IBotKindRuntime
{
    private readonly ConcurrentDictionary<Guid, decimal> _lastPrice = new();

    public BotKind Kind => BotKind.Grid;

    public async Task EvaluateAsync(BotKindContext ctx, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

        var bot = await BotKindHelpers.LoadActiveBotAsync(db, ctx.BotId, cancellationToken);
        if (bot is null) return;
        var cfg = BotKindConfigCodec.ParseGrid(bot.KindConfigJson);
        if (cfg.UpperPrice <= cfg.LowerPrice || cfg.GridLevels < 2 || cfg.QuotePerGrid <= 0m) return;

        var price = ctx.Candle.Close;
        if (price <= 0m) return;

        var rungs = BuildRungs(cfg);
        var prev = _lastPrice.TryGetValue(ctx.BotId, out var p) ? p : price;
        _lastPrice[ctx.BotId] = price;

        if (prev == price) return;

        var executor = scope.ServiceProvider.GetRequiredService<ITradingDispatcher>();

        if (price < prev)
        {
            // Downward cross: BUY at every rung crossed.
            foreach (var rung in rungs)
            {
                if (prev > rung && price <= rung)
                {
                    var qty = cfg.QuotePerGrid / rung;
                    await executor.ExecuteAsync(new PaperOrderRequest(
                        ctx.BotId, null, ctx.SymbolId, OrderSide.Buy, qty, price, $"grid:buy@{rung:F4}"), cancellationToken);
                    await botBus.PublishAsync(new BotEvent(ctx.BotId, "order",
                        $"GRID buy qty={qty:F6} @ {price:F4} (rung {rung:F4})", DateTimeOffset.UtcNow), cancellationToken);
                }
            }
        }
        else
        {
            // Upward cross: SELL at every rung crossed (closes most recent buy size).
            foreach (var rung in rungs)
            {
                if (prev < rung && price >= rung)
                {
                    var openQty = await db.Positions
                        .Where(po => po.BotId == ctx.BotId && po.SymbolId == ctx.SymbolId && po.Status == PositionStatus.Open)
                        .SumAsync(po => (decimal?)po.Quantity, cancellationToken) ?? 0m;
                    if (openQty <= 0m) continue;
                    var sellQty = Math.Min(openQty, cfg.QuotePerGrid / rung);
                    await executor.ExecuteAsync(new PaperOrderRequest(
                        ctx.BotId, null, ctx.SymbolId, OrderSide.Sell, sellQty, price, $"grid:sell@{rung:F4}"), cancellationToken);
                    await botBus.PublishAsync(new BotEvent(ctx.BotId, "order",
                        $"GRID sell qty={sellQty:F6} @ {price:F4} (rung {rung:F4})", DateTimeOffset.UtcNow), cancellationToken);
                }
            }
        }
    }

    private static decimal[] BuildRungs(GridConfig cfg)
    {
        var step = (cfg.UpperPrice - cfg.LowerPrice) / (cfg.GridLevels - 1);
        var rungs = new decimal[cfg.GridLevels];
        for (var i = 0; i < cfg.GridLevels; i++) rungs[i] = cfg.LowerPrice + step * i;
        return rungs;
    }
}
