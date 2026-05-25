using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading.LiveTrading;

/// <summary>
/// Single entry point for runtime + workers to place orders. Routes by Bot.RunMode:
///   PaperTrading → IPaperOrderExecutor
///   LiveTrading  → ILiveTradingGate, then by linked api-key Exchange.Code:
///     binance-futures-testnet → LiveFuturesExecutor (server-side SL/TP via conditional orders)
///     binance-spot-testnet    → LiveSpotExecutor (in-process SL/TP via PositionMonitor)
///   Off / ScanOnly → reject (callers should pre-filter; defensive here)
/// </summary>
public sealed class TradingDispatcher(
    QuantFlowBotsDbContext db,
    IPaperOrderExecutor paper,
    LiveFuturesExecutor liveFutures,
    LiveSpotExecutor liveSpot,
    ILiveTradingGate gate,
    IBotEventBus bus,
    ILogger<TradingDispatcher> logger) : ITradingDispatcher
{
    public async Task<PaperOrderResult> ExecuteAsync(PaperOrderRequest request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
        if (bot is null) return Reject("bot_not_found");

        switch (bot.RunMode)
        {
            case BotRunMode.PaperTrading:
                return await paper.ExecuteAsync(request, cancellationToken);

            case BotRunMode.LiveTrading:
                var check = await gate.EvaluateAsync(request.BotId, cancellationToken);
                if (!check.Allowed)
                {
                    await bus.PublishAsync(new BotEvent(request.BotId, "order",
                        $"live order blocked: {check.Reason}", DateTimeOffset.UtcNow), cancellationToken);
                    logger.LogWarning("Live order blocked bot={BotId}: {Reason}", request.BotId, check.Reason);
                    return Reject(check.Reason);
                }

                // Need the api-key's exchange code to pick the right executor.
                var exchangeCode = await db.ApiKeys
                    .Where(k => k.Id == bot.ApiKeyId)
                    .Select(k => k.Exchange!.Code)
                    .FirstOrDefaultAsync(cancellationToken);
                return exchangeCode switch
                {
                    "binance-futures-testnet" => await liveFutures.ExecuteAsync(request, cancellationToken),
                    "binance-spot-testnet"    => await liveSpot.ExecuteAsync(request, cancellationToken),
                    _ => Reject($"unsupported_exchange:{exchangeCode}"),
                };

            default:
                logger.LogInformation("Dispatcher dropped order bot={BotId} runMode={Mode}", request.BotId, bot.RunMode);
                return Reject($"run_mode_{bot.RunMode}");
        }

        static PaperOrderResult Reject(string? reason)
            => new(Guid.Empty, OrderStatus.Rejected, 0, 0, 0, reason);
    }
}
