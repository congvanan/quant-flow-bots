using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading;

public sealed class RiskEngine(
    QuantFlowBotsDbContext db,
    IBotEventBus botBus,
    ILogger<RiskEngine> logger) : IRiskEngine
{
    public async Task<RiskCheckResult> EvaluateAsync(RiskCheckRequest req, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == req.BotId, cancellationToken);
        if (bot is null) return Reject("bot_not_found", 0m);

        // SELL closes existing position — only check that one exists.
        if (req.Side == OrderSide.Sell)
        {
            var hasOpen = await db.Positions.AnyAsync(
                p => p.BotId == req.BotId && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open,
                cancellationToken);
            if (!hasOpen) return Reject("no_open_position", 0m);
            var openPos = await db.Positions.FirstAsync(
                p => p.BotId == req.BotId && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open,
                cancellationToken);
            return new RiskCheckResult(true, null, openPos.Quantity, null, null);
        }

        // BUY: full risk gate
        if (bot.State != BotState.Running)
            return await BlockAsync(bot, "bot_not_running", $"Bot state={bot.State}", cancellationToken);

        if (bot.RunMode == BotRunMode.Off)
            return await BlockAsync(bot, "run_mode_off", "RunMode=Off — bot is disabled", cancellationToken);

        if (bot.RunMode == BotRunMode.ScanOnly)
            return await BlockAsync(bot, "scan_only", "RunMode=ScanOnly — signals only, no orders", cancellationToken);

        // LiveTrading is now allowed; the actual live gate (api key, withdraw block,
        // validation freshness, futures-testnet only) is enforced in TradingDispatcher.

        if (bot.KillSwitchTrippedAt is not null)
            return await BlockAsync(bot, "kill_switch_tripped",
                $"Kill switch active: {bot.KillSwitchReason ?? "manual"}", cancellationToken);

        // Max open positions across the bot
        var openCount = await db.Positions
            .CountAsync(p => p.BotId == bot.Id && p.Status == PositionStatus.Open, cancellationToken);
        if (openCount >= bot.MaxOpenPositions)
            return await BlockAsync(bot, "max_open_positions",
                $"Open={openCount} >= max={bot.MaxOpenPositions}", cancellationToken);

        // Don't stack into the same symbol unless explicitly allowed
        var sameSymOpen = await db.Positions
            .AnyAsync(p => p.BotId == bot.Id && p.SymbolId == req.SymbolId && p.Status == PositionStatus.Open, cancellationToken);
        if (sameSymOpen)
            return await BlockAsync(bot, "symbol_position_exists",
                "Position already open on this symbol", cancellationToken);

        var today = DateTimeOffset.UtcNow.Date;
        var dayStart = new DateTimeOffset(today, TimeSpan.Zero);

        // Daily loss limit
        if (bot.DailyLossStopPercent > 0m && bot.BaseEquityUsdt > 0m)
        {
            var dayPnl = await db.Positions
                .Where(p => p.BotId == bot.Id && p.Status == PositionStatus.Closed
                    && p.ClosedAt != null && p.ClosedAt >= dayStart)
                .SumAsync(p => (decimal?)p.RealizedPnl, cancellationToken) ?? 0m;
            var lossPct = dayPnl < 0 ? -dayPnl / bot.BaseEquityUsdt * 100m : 0m;
            if (lossPct >= bot.DailyLossStopPercent)
            {
                await TripAsync(bot, $"daily_loss_limit pnl={dayPnl:F2} ({lossPct:F2}%)", cancellationToken);
                return new RiskCheckResult(false, "daily_loss_limit", 0m, null, null);
            }
        }

        // Consecutive losses
        if (bot.MaxConsecutiveLosses > 0)
        {
            var recent = await db.Positions
                .Where(p => p.BotId == bot.Id && p.Status == PositionStatus.Closed && p.ClosedAt != null)
                .OrderByDescending(p => p.ClosedAt)
                .Take(bot.MaxConsecutiveLosses)
                .Select(p => p.RealizedPnl)
                .ToListAsync(cancellationToken);
            if (recent.Count >= bot.MaxConsecutiveLosses && recent.All(x => x < 0))
            {
                await TripAsync(bot, $"max_consecutive_losses={bot.MaxConsecutiveLosses}", cancellationToken);
                return new RiskCheckResult(false, "max_consecutive_losses", 0m, null, null);
            }
        }

        // Cooldown after most recent loss
        if (bot.CooldownAfterLossMinutes > 0)
        {
            var lastLoss = await db.Positions
                .Where(p => p.BotId == bot.Id && p.Status == PositionStatus.Closed && p.ClosedAt != null && p.RealizedPnl < 0)
                .OrderByDescending(p => p.ClosedAt)
                .Select(p => p.ClosedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (lastLoss.HasValue)
            {
                var until = lastLoss.Value.AddMinutes(bot.CooldownAfterLossMinutes);
                if (DateTimeOffset.UtcNow < until)
                {
                    var mins = (until - DateTimeOffset.UtcNow).TotalMinutes;
                    return await BlockAsync(bot, "cooldown_after_loss",
                        $"Cooldown active, {mins:F1}m remaining", cancellationToken);
                }
            }
        }

        // Position sizing
        var slPct = bot.DefaultStopLossPercent;
        decimal qty;
        if (bot.RiskPerTradePercent is decimal riskPct && riskPct > 0m && slPct is decimal sl && sl > 0m && bot.BaseEquityUsdt > 0m)
        {
            var riskUsd = bot.BaseEquityUsdt * (riskPct / 100m);
            var slDistance = req.Price * (sl / 100m);
            qty = slDistance > 0 ? riskUsd / slDistance : 0m;
            if (bot.MaxPositionSize > 0m && qty > bot.MaxPositionSize) qty = bot.MaxPositionSize;
        }
        else
        {
            qty = bot.MaxPositionSize > 0m ? bot.MaxPositionSize : 0m;
        }
        if (qty <= 0m)
            return await BlockAsync(bot, "invalid_quantity", "Computed position size <= 0", cancellationToken);

        var stopPrice = slPct is decimal s ? req.Price * (1m - s / 100m) : (decimal?)null;
        var tpPrice = bot.DefaultTakeProfitPercent is decimal t ? req.Price * (1m + t / 100m) : (decimal?)null;

        return new RiskCheckResult(true, null, qty, stopPrice, tpPrice);
    }

    public async Task<bool> TripKillSwitchAsync(Guid botId, string reason, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == botId, cancellationToken);
        if (bot is null) return false;
        await TripAsync(bot, reason, cancellationToken);
        return true;
    }

    public async Task<bool> ResetKillSwitchAsync(Guid botId, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == botId, cancellationToken);
        if (bot is null) return false;
        if (bot.KillSwitchTrippedAt is null) return true;
        bot.KillSwitchTrippedAt = null;
        var prev = bot.KillSwitchReason;
        bot.KillSwitchReason = null;
        bot.UpdatedAt = DateTimeOffset.UtcNow;
        db.RiskEvents.Add(new RiskEvent
        {
            UserId = bot.UserId,
            BotId = bot.Id,
            EventType = "kill_switch_reset",
            Severity = "info",
            Message = $"Kill switch reset (was: {prev})",
            ActionTaken = "reset_kill_switch",
        });
        await db.SaveChangesAsync(cancellationToken);
        await botBus.PublishAsync(new BotEvent(bot.Id, "risk",
            $"kill switch reset (was: {prev})", DateTimeOffset.UtcNow), cancellationToken);
        return true;
    }

    private static RiskCheckResult Reject(string reason, decimal qty) =>
        new(false, reason, qty, null, null);

    private async Task<RiskCheckResult> BlockAsync(Bot bot, string reason, string message, CancellationToken cancellationToken)
    {
        db.RiskEvents.Add(new RiskEvent
        {
            UserId = bot.UserId,
            BotId = bot.Id,
            EventType = reason,
            Severity = "warn",
            Message = message,
            ActionTaken = "block_order",
        });
        await db.SaveChangesAsync(cancellationToken);
        await botBus.PublishAsync(new BotEvent(bot.Id, "risk", $"blocked: {reason} — {message}", DateTimeOffset.UtcNow), cancellationToken);
        logger.LogWarning("RiskEngine blocked order for bot {BotId}: {Reason} — {Message}", bot.Id, reason, message);
        return new RiskCheckResult(false, reason, 0m, null, null);
    }

    private async Task TripAsync(Bot bot, string reason, CancellationToken cancellationToken)
    {
        if (!bot.KillSwitchEnabled) return;
        bot.KillSwitchTrippedAt = DateTimeOffset.UtcNow;
        bot.KillSwitchReason = reason;
        bot.UpdatedAt = DateTimeOffset.UtcNow;
        db.RiskEvents.Add(new RiskEvent
        {
            UserId = bot.UserId,
            BotId = bot.Id,
            EventType = "kill_switch_tripped",
            Severity = "critical",
            Message = $"Kill switch tripped: {reason}",
            ActionTaken = "trip_kill_switch",
        });
        await db.SaveChangesAsync(cancellationToken);
        await botBus.PublishAsync(new BotEvent(bot.Id, "risk",
            $"KILL SWITCH TRIPPED: {reason}", DateTimeOffset.UtcNow), cancellationToken);
        logger.LogCritical("RiskEngine tripped kill switch for bot {BotId}: {Reason}", bot.Id, reason);
    }
}
