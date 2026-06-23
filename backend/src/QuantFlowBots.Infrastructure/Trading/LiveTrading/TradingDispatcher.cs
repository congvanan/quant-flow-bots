using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Entities;
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
///   Off / ScanOnly → reject (callers should pre-filter; defensive here)
///
/// MULTI-ACCOUNT (2026-06-22 — xem [[project-quantflow-multi-account]]):
///   Nếu bot có >=1 BotAccount active → fan-out: 1 tín hiệu vào lệnh được nhân ra mỗi account,
///   size độc lập theo vốn riêng (Independent model):
///       accountQty = baseQty × (account.BaseEquityUsdt / bot.BaseEquityUsdt) × account.Weight
///   Mỗi account qua gate riêng (live), kill-switch riêng, daily-loss riêng; account nào fail/
///   quá nhỏ min-notional thì SKIP (log) — không làm hỏng cả fan-out. Lệnh con tag ApiKeyId để
///   đóng + tính PnL riêng. Bot KHÔNG có BotAccount → đường single-account legacy (Bot.ApiKeyId).
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
                return await RunAsync(request, bot, isLive: false, cancellationToken);

            case BotRunMode.LiveTrading:
                // Bot-level entry filters (context filters + basis guard) chạy MỘT lần cho cả
                // fan-out — chúng gắn symbol/bối cảnh, không gắn account. Chỉ áp cho Buy (entry).
                if (request.Side == OrderSide.Buy)
                {
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
                }

                return await RunAsync(request, bot, isLive: true, cancellationToken);

            default:
                logger.LogInformation("Dispatcher dropped order bot={BotId} runMode={Mode}", request.BotId, bot.RunMode);
                return Reject($"run_mode_{bot.RunMode}");
        }
    }

    /// <summary>Chọn single-account legacy hay multi-account fan-out, theo Buy/Sell.</summary>
    private async Task<PaperOrderResult> RunAsync(PaperOrderRequest req, Bot bot, bool isLive, CancellationToken ct)
    {
        var accounts = await db.BotAccounts.AsNoTracking()
            .Where(a => a.BotId == bot.Id && a.IsActive)
            .OrderByDescending(a => a.Weight)
            .ToListAsync(ct);

        // Không cấu hình multi-account → giữ nguyên hành vi cũ.
        if (accounts.Count == 0)
        {
            if (isLive)
            {
                var single = await gate.EvaluateAsync(req.BotId, ct);
                if (!single.Allowed)
                {
                    await bus.PublishAsync(new BotEvent(req.BotId, "order",
                        $"live order blocked: {single.Reason}", DateTimeOffset.UtcNow), ct);
                    return Reject(single.Reason);
                }
                var exCode = await db.ApiKeys.Where(k => k.Id == bot.ApiKeyId)
                    .Select(k => k.Exchange!.Code).FirstOrDefaultAsync(ct);
                if (exCode is not null && !ExchangeMatchesMarket(exCode, bot.ExecutionMarket))
                    return Reject($"market_mismatch:{exCode}_vs_{bot.ExecutionMarket}");
            }
            return await RouteOneAsync(req, bot, isLive, ct);
        }

        return req.Side == OrderSide.Buy
            ? await FanOutEntryAsync(req, bot, accounts, isLive, ct)
            : await FanOutExitAsync(req, bot, isLive, ct);
    }

    private async Task<PaperOrderResult> FanOutEntryAsync(
        PaperOrderRequest req, Bot bot, List<BotAccount> accounts, bool isLive, CancellationToken ct)
    {
        var baseEquity = bot.BaseEquityUsdt > 0m ? bot.BaseEquityUsdt : 1m;
        var dayStart = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var results = new List<PaperOrderResult>();
        var placed = 0;

        foreach (var acct in accounts)
        {
            // 1) Per-account kill-switch
            if (acct.KillSwitchTrippedAt is not null)
            {
                await SkipAsync(bot.Id, acct, $"kill-switch active: {acct.KillSwitchReason}", ct);
                continue;
            }

            // 2) Live gate + exchange-market match cho key của account này
            if (isLive)
            {
                var g = await gate.EvaluateKeyAsync(acct.ApiKeyId, ct);
                if (!g.Allowed) { await SkipAsync(bot.Id, acct, $"gate:{g.Reason}", ct); continue; }
                var exCode = await db.ApiKeys.Where(k => k.Id == acct.ApiKeyId)
                    .Select(k => k.Exchange!.Code).FirstOrDefaultAsync(ct);
                if (exCode is not null && !ExchangeMatchesMarket(exCode, bot.ExecutionMarket))
                { await SkipAsync(bot.Id, acct, $"market_mismatch:{exCode}", ct); continue; }
            }

            // 3) Per-account daily-loss stop (dùng % của bot, áp lên vốn riêng account)
            if (await DailyLossTrippedAsync(bot, acct, dayStart, ct)) continue;

            // 4) Không chồng lệnh cùng symbol trên cùng account
            var alreadyOpen = await db.Positions.AnyAsync(p =>
                p.BotId == bot.Id && p.SymbolId == req.SymbolId &&
                p.Status == PositionStatus.Open && p.ApiKeyId == acct.ApiKeyId, ct);
            if (alreadyOpen) { await SkipAsync(bot.Id, acct, "position_already_open", ct); continue; }

            // 5) Independent sizing theo vốn + weight
            var qty = req.Quantity * (acct.BaseEquityUsdt / baseEquity) * acct.Weight;
            if (qty <= 0m) { await SkipAsync(bot.Id, acct, "qty<=0", ct); continue; }

            var childReq = req with { ApiKeyId = acct.ApiKeyId, Quantity = qty };
            var r = await RouteOneAsync(childReq, bot, isLive, ct);
            results.Add(r);
            if (r.Status is OrderStatus.Filled or OrderStatus.PartiallyFilled) placed++;
            else
                // Min-notional/filter reject → skip account, không hỏng fan-out (đã log trong executor).
                // Reject reason được nhồi vào field PositionId theo convention của PaperOrderResult.
                logger.LogInformation("Fan-out account {Label} ({Key}) not filled: {Reason}",
                    acct.Label, acct.ApiKeyId, r.PositionId);
        }

        if (placed == 0) return Reject("all_accounts_skipped");
        var filledQty = results.Sum(r => r.FilledQuantity);
        var pnl = results.Sum(r => r.RealizedPnl);
        var avg = results.Where(r => r.FilledQuantity > 0).Select(r => r.AveragePrice).DefaultIfEmpty(0m).Average();
        await bus.PublishAsync(new BotEvent(bot.Id, "order",
            $"fan-out entry: {placed}/{accounts.Count} accounts filled, totalQty={filledQty:F6}", DateTimeOffset.UtcNow), ct);
        return new PaperOrderResult(Guid.Empty, OrderStatus.Filled, filledQty, avg, pnl, null);
    }

    private async Task<PaperOrderResult> FanOutExitAsync(PaperOrderRequest req, Bot bot, bool isLive, CancellationToken ct)
    {
        // Close targeted (1 vị thế cụ thể, vd từ PositionMonitor/RiskGateEnforcer) → route thẳng.
        if (req.PositionId is not null)
            return await RouteOneAsync(req, bot, isLive, ct);

        // Close bot-level (vd strategy exit signal): đóng MỌI vị thế mở của bot trên symbol này,
        // mỗi cái bằng đúng account + quantity của nó.
        var open = await db.Positions.AsNoTracking()
            .Where(p => p.BotId == bot.Id && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open)
            .Select(p => new { p.Id, p.ApiKeyId, p.Quantity })
            .ToListAsync(ct);
        if (open.Count == 0) return Reject("no_open_position");

        var results = new List<PaperOrderResult>();
        foreach (var p in open)
        {
            var childReq = req with { ApiKeyId = p.ApiKeyId, PositionId = p.Id, Quantity = p.Quantity };
            results.Add(await RouteOneAsync(childReq, bot, isLive, ct));
        }
        var pnl = results.Sum(r => r.RealizedPnl);
        var filledQty = results.Sum(r => r.FilledQuantity);
        return new PaperOrderResult(Guid.Empty, OrderStatus.Filled, filledQty, 0m, pnl, null);
    }

    /// <summary>Route 1 lệnh con tới executor đúng (paper / live futures / live spot).</summary>
    private async Task<PaperOrderResult> RouteOneAsync(PaperOrderRequest req, Bot bot, bool isLive, CancellationToken ct)
    {
        if (!isLive) return await paper.ExecuteAsync(req, ct);
        return bot.ExecutionMarket switch
        {
            MarketKind.Futures => await liveFutures.ExecuteAsync(req, ct),
            MarketKind.Spot    => await liveSpot.ExecuteAsync(req, ct),
            _ => Reject($"unsupported_market:{bot.ExecutionMarket}"),
        };
    }

    /// <summary>Tính PnL hôm nay của account; trip kill-switch + skip nếu vượt ngưỡng % của bot.</summary>
    private async Task<bool> DailyLossTrippedAsync(Bot bot, BotAccount acct, DateTimeOffset dayStart, CancellationToken ct)
    {
        if (bot.DailyLossStopPercent <= 0m || acct.BaseEquityUsdt <= 0m) return false;
        var dayPnl = await db.Positions
            .Where(p => p.BotId == bot.Id && p.ApiKeyId == acct.ApiKeyId
                && p.Status == PositionStatus.Closed && p.ClosedAt != null && p.ClosedAt >= dayStart)
            .SumAsync(p => (decimal?)p.RealizedPnl, ct) ?? 0m;
        var lossPct = dayPnl < 0 ? -dayPnl / acct.BaseEquityUsdt * 100m : 0m;
        if (lossPct < bot.DailyLossStopPercent) return false;

        var tracked = await db.BotAccounts.FirstOrDefaultAsync(a => a.Id == acct.Id, ct);
        if (tracked is not null && tracked.KillSwitchTrippedAt is null)
        {
            tracked.KillSwitchTrippedAt = DateTimeOffset.UtcNow;
            tracked.KillSwitchReason = $"daily_loss_limit pnl={dayPnl:F2} ({lossPct:F2}%)";
            tracked.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        await bus.PublishAsync(new BotEvent(bot.Id, "risk",
            $"account {acct.Label}: daily-loss tripped pnl={dayPnl:F2} ({lossPct:F2}%)", DateTimeOffset.UtcNow), ct);
        logger.LogWarning("Account {Label} ({Key}) daily-loss tripped for bot {BotId}", acct.Label, acct.ApiKeyId, bot.Id);
        return true;
    }

    private async Task SkipAsync(Guid botId, BotAccount acct, string reason, CancellationToken ct)
    {
        logger.LogInformation("Fan-out skip account {Label} ({Key}): {Reason}", acct.Label, acct.ApiKeyId, reason);
        await bus.PublishAsync(new BotEvent(botId, "order",
            $"account {acct.Label} skipped: {reason}", DateTimeOffset.UtcNow), ct);
    }

    private static PaperOrderResult Reject(string? reason)
        => new(Guid.Empty, OrderStatus.Rejected, 0, 0, 0, reason);

    private static bool ExchangeMatchesMarket(string exchangeCode, MarketKind market) => (exchangeCode, market) switch
    {
        ("binance-futures-testnet", MarketKind.Futures) => true,
        ("binance-spot-testnet",    MarketKind.Spot)    => true,
        _ => false,
    };
}
