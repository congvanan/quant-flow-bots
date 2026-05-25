using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading.BotKinds;

/// <summary>
/// DCA: open initial base order, layer safety orders each time price drops PriceStepPercent
/// below latest average entry. Close entire position on TakeProfitPercent above average entry.
/// MaxSafetyOrders caps total layered fills.
/// </summary>
public sealed class DcaBotKindRuntime(
    IServiceScopeFactory scopeFactory,
    IBotEventBus botBus,
    ILogger<DcaBotKindRuntime> logger) : IBotKindRuntime
{
    public BotKind Kind => BotKind.Dca;

    public async Task EvaluateAsync(BotKindContext ctx, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

        var bot = await BotKindHelpers.LoadActiveBotAsync(db, ctx.BotId, cancellationToken);
        if (bot is null) return;
        var cfg = BotKindConfigCodec.ParseDca(bot.KindConfigJson);
        var price = ctx.Candle.Close;
        if (price <= 0m) return;

        var pos = await db.Positions
            .Where(p => p.BotId == ctx.BotId && p.SymbolId == ctx.SymbolId && p.Status == PositionStatus.Open)
            .FirstOrDefaultAsync(cancellationToken);

        var executor = scope.ServiceProvider.GetRequiredService<ITradingDispatcher>();
        var risk = scope.ServiceProvider.GetRequiredService<IRiskEngine>();

        if (pos is null)
        {
            // Initial base order.
            var check = await risk.EvaluateAsync(new RiskCheckRequest(
                ctx.BotId, ctx.UserId, ctx.SymbolId, OrderSide.Buy, price), cancellationToken);
            if (!check.Approved)
            {
                logger.LogInformation("DCA base buy blocked for bot {BotId}: {Reason}", ctx.BotId, check.Reason);
                return;
            }
            var qty = cfg.BaseQuoteAmount / price;
            await executor.ExecuteAsync(new PaperOrderRequest(
                ctx.BotId, null, ctx.SymbolId, OrderSide.Buy, qty, price, "dca:base"), cancellationToken);
            await botBus.PublishAsync(new BotEvent(ctx.BotId, "order",
                $"DCA base buy qty={qty:F6} @ {price:F4}", DateTimeOffset.UtcNow), cancellationToken);
            return;
        }

        // Existing position.
        var safetyDone = CountSafetyOrders(db, ctx.BotId, pos.OpenedAt);

        // Take-profit gate.
        var tpPrice = pos.EntryPrice * (1m + cfg.TakeProfitPercent / 100m);
        if (price >= tpPrice)
        {
            await executor.ExecuteAsync(new PaperOrderRequest(
                ctx.BotId, null, ctx.SymbolId, OrderSide.Sell, pos.Quantity, price, "dca:tp"), cancellationToken);
            await botBus.PublishAsync(new BotEvent(ctx.BotId, "auto_close",
                $"DCA TP @ {price:F4} qty={pos.Quantity:F6}", DateTimeOffset.UtcNow), cancellationToken);
            return;
        }

        // Safety-order trigger.
        if (safetyDone >= cfg.MaxSafetyOrders) return;
        var nextStepCount = safetyDone + 1;
        var triggerDrop = cfg.PriceStepPercent / 100m * nextStepCount;
        var triggerPrice = pos.EntryPrice * (1m - triggerDrop);
        if (price > triggerPrice) return;

        var quote = cfg.SafetyQuoteAmount * (decimal)Math.Pow((double)cfg.VolumeScale, safetyDone);
        var addQty = quote / price;
        await executor.ExecuteAsync(new PaperOrderRequest(
            ctx.BotId, null, ctx.SymbolId, OrderSide.Buy, addQty, price, $"dca:so{nextStepCount}"), cancellationToken);
        await botBus.PublishAsync(new BotEvent(ctx.BotId, "order",
            $"DCA SO#{nextStepCount} qty={addQty:F6} @ {price:F4}", DateTimeOffset.UtcNow), cancellationToken);
    }

    private static int CountSafetyOrders(QuantFlowBotsDbContext db, Guid botId, DateTimeOffset since) =>
        db.Orders.Count(o => o.BotId == botId && o.Side == OrderSide.Buy && o.CreatedAt >= since) - 1;
}
