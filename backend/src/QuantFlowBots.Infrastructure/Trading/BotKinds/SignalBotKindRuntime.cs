using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading.BotKinds;

public sealed class SignalBotKindRuntime(
    IServiceScopeFactory scopeFactory,
    ISignalEventBus signalBus,
    IBotEventBus botBus,
    ILogger<SignalBotKindRuntime> logger) : IBotKindRuntime
{
    public BotKind Kind => BotKind.Signal;

    public async Task EvaluateAsync(BotKindContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.Strategy is null) return;
        if (ctx.History.Count < ctx.Strategy.WarmupBars) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

        var botRow = await BotKindHelpers.LoadActiveBotAsync(db, ctx.BotId, cancellationToken);
        if (botRow is null) return;

        var openPos = await db.Positions
            .Where(p => p.BotId == ctx.BotId && p.SymbolId == ctx.SymbolId && p.Status == PositionStatus.Open)
            .FirstOrDefaultAsync(cancellationToken);

        var stratCtx = new SignalContext(
            ctx.SymbolCode, ctx.Candle.CloseTime, ctx.History,
            openPos?.Quantity, openPos?.EntryPrice, ctx.MaxPositionSize);

        var decision = ctx.Strategy.OnCandle(ctx.Candle, stratCtx);
        if (decision is null) return;

        var signal = new Signal
        {
            StrategyId = botRow.StrategyId,
            SymbolId = ctx.SymbolId,
            Type = decision.Type,
            Side = decision.Side,
            Price = decision.Price ?? ctx.Candle.Close,
            Score = decision.Score,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(decision.Metadata ?? new Dictionary<string, object?>()),
            GeneratedAt = DateTimeOffset.UtcNow,
        };
        db.Signals.Add(signal);
        await db.SaveChangesAsync(cancellationToken);

        await signalBus.PublishAsync(new SignalEvent(
            signal.Id, signal.StrategyId, ctx.SymbolCode,
            decision.Type.ToString(), decision.Side?.ToString(),
            signal.Price, signal.Score, signal.GeneratedAt), cancellationToken);

        await botBus.PublishAsync(new BotEvent(ctx.BotId, "signal",
            $"{decision.Type} {decision.Side} @ {signal.Price:F4} — {decision.Reason}", DateTimeOffset.UtcNow), cancellationToken);

        logger.LogDebug("SignalBotKindRuntime emitted {Type} {Side} for bot {BotId}", decision.Type, decision.Side, ctx.BotId);
    }

    private sealed class SignalContext(
        string symbol,
        DateTimeOffset now,
        IReadOnlyList<CandleData> history,
        decimal? openQty,
        decimal? entryPrice,
        decimal cash) : IStrategyContext
    {
        public string Symbol => symbol;
        public DateTimeOffset Now => now;
        public IReadOnlyList<CandleData> History => history;
        public decimal? OpenPositionQuantity => openQty;
        public decimal? OpenPositionEntryPrice => entryPrice;
        public decimal Cash => cash;
    }
}
