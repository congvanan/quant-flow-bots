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
///   LiveTrading  → ILiveTradingGate, then by <see cref="Bot.ExecutionMarket"/> (declarative;
///     chốt 2026-06-03 — xem [[project-quantflow-market-axis]]):
///       Futures → LiveFuturesExecutor (server-side SL/TP via conditional orders)
///       Spot    → LiveSpotExecutor (in-process SL/TP via PositionMonitor)
///     Defense-in-depth: api-key Exchange.Code phải match ExecutionMarket — nếu lệch (vd
///     futures key gắn vào Spot bot do dữ liệu cũ trước migration backfill), reject thay vì
///     đoán. BotsEndpoints validate cùng rule khi create/update.
///   Off / ScanOnly → reject (callers should pre-filter; defensive here)
/// </summary>
public sealed class TradingDispatcher(
    QuantFlowBotsDbContext db,
    IPaperOrderExecutor paper,
    LiveFuturesExecutor liveFutures,
    LiveSpotExecutor liveSpot,
    ILiveTradingGate gate,
    IBasisGuard basisGuard,
    IContextFilterRunner contextFilters,
    IBotEventBus bus,
    ILogger<TradingDispatcher> logger) : ITradingDispatcher
{
    public async Task<PaperOrderResult> ExecuteAsync(PaperOrderRequest request, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.Include(b => b.Symbol).AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BotId, cancellationToken);
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

                // Defense-in-depth: nếu bot có ApiKey, exchange code phải khớp ExecutionMarket.
                // BotsEndpoints đã validate ở create/update; check lại ở runtime để bắt trường
                // hợp DB bị edit trực tiếp hoặc migration backfill miss row.
                var exchangeCode = await db.ApiKeys
                    .Where(k => k.Id == bot.ApiKeyId)
                    .Select(k => k.Exchange!.Code)
                    .FirstOrDefaultAsync(cancellationToken);
                if (exchangeCode is not null && !ExchangeMatchesMarket(exchangeCode, bot.ExecutionMarket))
                {
                    logger.LogError("Bot {BotId} ExecutionMarket={Market} but api key exchange={Exchange} — reject",
                        request.BotId, bot.ExecutionMarket, exchangeCode);
                    return Reject($"market_mismatch:{exchangeCode}_vs_{bot.ExecutionMarket}");
                }

                // ContextFilters (Phase 3): chạy AND-logic mọi filter user chọn trước cả basis guard.
                // Spot bot cũng được hưởng (filter là context risk chung, không gắn market).
                if (bot.Symbol is not null && !string.IsNullOrWhiteSpace(bot.ContextFiltersJson))
                {
                    var ctxRes = await contextFilters.CheckAsync(bot.ContextFiltersJson, bot.Symbol.Code, request.Side, cancellationToken);
                    if (!ctxRes.Ok)
                    {
                        await bus.PublishAsync(new BotEvent(request.BotId, "context_filter",
                            $"entry blocked by {ctxRes.BlockedBy}: {ctxRes.Reason}", DateTimeOffset.UtcNow), cancellationToken);
                        logger.LogInformation("Context filter blocked bot={BotId} symbol={Symbol} filter={Filter} reason={Reason}",
                            request.BotId, bot.Symbol.Code, ctxRes.BlockedBy, ctxRes.Reason);
                        return Reject($"context_filter:{ctxRes.BlockedBy}");
                    }
                }

                // BasisGuard: chỉ kiểm khi Futures + bot bật MaxBasisPct. Spot bot không cần
                // (không có basis), paper trading bỏ qua ở case trên (block chỉ live). Fail-open
                // khi cache thiếu data — xem [[project-quantflow-market-axis]].
                if (bot.ExecutionMarket == MarketKind.Futures && bot.MaxBasisPct is decimal maxBasis && bot.Symbol is not null)
                {
                    var basisCheck = await basisGuard.CheckAsync(bot.Symbol.Code, maxBasis, cancellationToken);
                    if (!basisCheck.Ok)
                    {
                        await bus.PublishAsync(new BotEvent(request.BotId, "basis_guard",
                            $"entry blocked: {basisCheck.Reason}", DateTimeOffset.UtcNow), cancellationToken);
                        logger.LogInformation("Basis guard blocked bot={BotId} symbol={Symbol} basis={Basis:F4}% reason={Reason}",
                            request.BotId, bot.Symbol.Code, basisCheck.BasisPct, basisCheck.Reason);
                        return Reject(basisCheck.Reason);
                    }
                }

                return bot.ExecutionMarket switch
                {
                    MarketKind.Futures => await liveFutures.ExecuteAsync(request, cancellationToken),
                    MarketKind.Spot    => await liveSpot.ExecuteAsync(request, cancellationToken),
                    _ => Reject($"unsupported_market:{bot.ExecutionMarket}"),
                };

            default:
                logger.LogInformation("Dispatcher dropped order bot={BotId} runMode={Mode}", request.BotId, bot.RunMode);
                return Reject($"run_mode_{bot.RunMode}");
        }

        static PaperOrderResult Reject(string? reason)
            => new(Guid.Empty, OrderStatus.Rejected, 0, 0, 0, reason);
    }

    private static bool ExchangeMatchesMarket(string exchangeCode, MarketKind market) => (exchangeCode, market) switch
    {
        ("binance-futures-testnet", MarketKind.Futures) => true,
        ("binance-spot-testnet",    MarketKind.Spot)    => true,
        _ => false,
    };
}
