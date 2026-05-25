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
/// Scalp: tight quote-amount entry on low-volatility candle (range&lt;=SpreadBpsMax),
/// immediate small TP / SL via Position monitor's normal SL/TP plumbing.
/// Per-bot cooldown prevents thrash.
/// </summary>
public sealed class ScalpBotKindRuntime(
    IServiceScopeFactory scopeFactory,
    IBotEventBus botBus,
    ILogger<ScalpBotKindRuntime> logger) : IBotKindRuntime
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _cooldowns = new();

    public BotKind Kind => BotKind.Scalp;

    public async Task EvaluateAsync(BotKindContext ctx, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

        var bot = await BotKindHelpers.LoadActiveBotAsync(db, ctx.BotId, cancellationToken);
        if (bot is null) return;
        var cfg = BotKindConfigCodec.ParseScalp(bot.KindConfigJson);

        var openPos = await db.Positions
            .AnyAsync(p => p.BotId == ctx.BotId && p.SymbolId == ctx.SymbolId && p.Status == PositionStatus.Open, cancellationToken);
        if (openPos) return; // PositionMonitor handles exits (use bot's DefaultTakeProfit/DefaultStopLoss)

        if (_cooldowns.TryGetValue(ctx.BotId, out var until) && DateTimeOffset.UtcNow < until) return;

        var c = ctx.Candle;
        if (c.High <= 0m || c.Low <= 0m) return;
        var rangeBps = (c.High - c.Low) / c.Close * 10000m;
        if (rangeBps < cfg.SpreadBpsMin || rangeBps > cfg.SpreadBpsMax) return;

        var price = c.Close;
        if (price <= 0m) return;

        var risk = scope.ServiceProvider.GetRequiredService<IRiskEngine>();
        var check = await risk.EvaluateAsync(new RiskCheckRequest(
            ctx.BotId, ctx.UserId, ctx.SymbolId, OrderSide.Buy, price), cancellationToken);
        if (!check.Approved)
        {
            logger.LogInformation("Scalp buy blocked for bot {BotId}: {Reason}", ctx.BotId, check.Reason);
            return;
        }

        var qty = cfg.QuoteAmount / price;
        var executor = scope.ServiceProvider.GetRequiredService<ITradingDispatcher>();
        await executor.ExecuteAsync(new PaperOrderRequest(
            ctx.BotId, null, ctx.SymbolId, OrderSide.Buy, qty, price, "scalp:entry"), cancellationToken);
        _cooldowns[ctx.BotId] = DateTimeOffset.UtcNow.AddSeconds(cfg.CooldownSeconds);
        await botBus.PublishAsync(new BotEvent(ctx.BotId, "order",
            $"SCALP buy qty={qty:F6} @ {price:F4} rangeBps={rangeBps:F1}", DateTimeOffset.UtcNow), cancellationToken);
    }
}
