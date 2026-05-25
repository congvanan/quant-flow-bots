using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading.BotKinds;

/// <summary>
/// Strategy-of-strategies: each BotKind owns its own decision flow on candle close.
/// Implementations are singletons, must be thread-safe, and must scope DbContext per call.
/// </summary>
public interface IBotKindRuntime
{
    BotKind Kind { get; }
    Task EvaluateAsync(BotKindContext ctx, CancellationToken cancellationToken);
}

public sealed record BotKindContext(
    Guid BotId,
    Guid UserId,
    int SymbolId,
    string SymbolCode,
    decimal MaxPositionSize,
    IStrategy? Strategy,
    CandleData Candle,
    IReadOnlyList<CandleData> History);

public static class BotKindHelpers
{
    public static async Task<Domain.Entities.Bot?> LoadActiveBotAsync(
        QuantFlowBotsDbContext db, Guid botId, CancellationToken ct)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == botId, ct);
        if (bot is null || bot.State != BotState.Running) return null;
        return bot;
    }
}
