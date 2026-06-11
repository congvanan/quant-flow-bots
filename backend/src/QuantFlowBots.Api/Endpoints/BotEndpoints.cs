using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Security;
using QuantFlowBots.Infrastructure.Trading;
using QuantFlowBots.Infrastructure.Trading.LiveTrading;

namespace QuantFlowBots.Api.Endpoints;

public static class BotEndpoints
{
    public sealed record TpLevelInput(decimal ProfitPercent, decimal ClosePercent);
    public sealed record CreateBotRequest(
        string Name,
        Guid StrategyId,
        string SymbolCode,
        decimal MaxPositionSize,
        string? Kind,
        string? KindConfigJson,
        Guid? ApiKeyId,
        int? Leverage,
        string? RunMode,
        string? SymbolFilterJson,
        decimal? BaseEquityUsdt,
        decimal? RiskPerTradePercent,
        decimal? DailyLossStopPercent,
        int? MaxOpenPositions,
        int? MaxConsecutiveLosses,
        int? CooldownAfterLossMinutes,
        bool? KillSwitchEnabled,
        string? StopLossKind,
        decimal? DefaultStopLossPercent,
        int? AtrPeriod,
        decimal? AtrMultiplier,
        decimal? DefaultTakeProfitPercent,
        IReadOnlyList<TpLevelInput>? TakeProfitLevels,
        decimal? DefaultTrailingStopPercent,
        bool? BreakEvenEnabled,
        decimal? BreakEvenTriggerPercent,
        decimal? BreakEvenOffsetPercent,
        // Market axis (xem [[project-quantflow-market-axis]]). TriggerMarket bỏ qua ở v1 — API
        // force = ExecutionMarket. Khi v2 mở cross-market, thêm advanced flag để cho phép lệch.
        string? ExecutionMarket,
        string? ContextFiltersJson,
        decimal? MaxBasisPct);
    public sealed record BotDto(
        Guid Id, string Name, string Mode, string State, string Kind, string? KindConfigJson, Guid? ApiKeyId, int Leverage, string RunMode, string? SymbolFilterJson,
        string ExecutionMarket, string TriggerMarket, string? ContextFiltersJson, decimal? MaxBasisPct,
        Guid StrategyId, string? StrategyKind,
        int SymbolId, string SymbolCode,
        decimal BaseEquityUsdt, decimal MaxPositionSize, decimal? RiskPerTradePercent,
        decimal DailyLossStopPercent, int MaxOpenPositions, int MaxConsecutiveLosses, int CooldownAfterLossMinutes,
        bool KillSwitchEnabled, DateTimeOffset? KillSwitchTrippedAt, string? KillSwitchReason,
        string StopLossKind, decimal? DefaultStopLossPercent, int AtrPeriod, decimal AtrMultiplier,
        decimal? DefaultTakeProfitPercent, string? TakeProfitLevelsJson, decimal? DefaultTrailingStopPercent,
        bool BreakEvenEnabled, decimal? BreakEvenTriggerPercent, decimal BreakEvenOffsetPercent,
        string? LastError, DateTimeOffset CreatedAt);
    public sealed record UpdateBotRiskRequest(
        string? Kind,
        string? KindConfigJson,
        Guid? ApiKeyId,
        bool? UnlinkApiKey,
        int? Leverage,
        string? RunMode,
        string? SymbolFilterJson,
        decimal? BaseEquityUsdt,
        decimal? RiskPerTradePercent,
        decimal? DailyLossStopPercent,
        int? MaxOpenPositions,
        int? MaxConsecutiveLosses,
        int? CooldownAfterLossMinutes,
        bool? KillSwitchEnabled,
        string? StopLossKind,
        decimal? DefaultStopLossPercent,
        int? AtrPeriod,
        decimal? AtrMultiplier,
        decimal? DefaultTakeProfitPercent,
        IReadOnlyList<TpLevelInput>? TakeProfitLevels,
        decimal? DefaultTrailingStopPercent,
        bool? BreakEvenEnabled,
        decimal? BreakEvenTriggerPercent,
        decimal? BreakEvenOffsetPercent,
        string? ExecutionMarket,
        string? ContextFiltersJson,
        decimal? MaxBasisPct);
    public sealed record RiskEventDto(
        Guid Id, Guid? BotId, string EventType, string Severity,
        string Message, string? ActionTaken, DateTimeOffset CreatedAt);
    public sealed record TripKillSwitchRequest(string Reason);
    public sealed record OrderDto(Guid Id, string Side, string Status, decimal Price, decimal Quantity, decimal Commission, decimal RealizedPnl, DateTimeOffset CreatedAt);
    public sealed record PositionDto(
        Guid Id, string Side, string Status, decimal Quantity, decimal OriginalQuantity, decimal EntryPrice, decimal? ExitPrice,
        decimal? StopLossPrice, decimal? TakeProfitPrice, decimal? TrailingStopPercent,
        string? TakeProfitLevelsJson, bool BreakEvenTriggered,
        decimal RealizedPnl, string? CloseReason, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt);
    public sealed record SignalDto(Guid Id, string Type, string? Side, decimal Price, decimal Score, string PayloadJson, DateTimeOffset GeneratedAt);
    public sealed record EquityPoint(DateTimeOffset At, decimal Equity);
    public sealed record BotStatsDto(
        Guid BotId,
        string Name,
        decimal BaseEquity,
        decimal CurrentEquity,
        decimal TotalRealizedPnl,
        decimal UnrealizedPnl,
        decimal TotalReturnPercent,
        int TotalTrades,
        int WinningTrades,
        int LosingTrades,
        int OpenPositions,
        decimal WinRatePercent,
        decimal AverageWin,
        decimal AverageLoss,
        decimal LargestWin,
        decimal LargestLoss,
        decimal ProfitFactor,
        decimal MaxDrawdownPercent,
        decimal Expectancy,
        decimal PnlToday,
        decimal Pnl7d,
        decimal Pnl30d,
        int TradesToday,
        DateTimeOffset? FirstTradeAt,
        DateTimeOffset? LastTradeAt,
        IReadOnlyList<EquityPoint> EquityCurve);
    public sealed record BotStatsRowDto(
        Guid BotId,
        string Name,
        string SymbolCode,
        string RunMode,
        string State,
        decimal BaseEquity,
        decimal CurrentEquity,
        decimal TotalRealizedPnl,
        decimal TotalReturnPercent,
        int TotalTrades,
        decimal WinRatePercent,
        decimal MaxDrawdownPercent,
        decimal PnlToday,
        decimal Pnl7d,
        int OpenPositions);

    public static IEndpointRouteBuilder MapBots(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/bots").WithTags("bots");

        grp.MapGet("/kinds", () => Results.Ok(Enum.GetNames<BotKind>())).AllowAnonymous();

        grp.MapGet("/status", () => Results.Ok(new
        {
            mode = "paper+live_testnet",
            liveTradingEnabled = true,
            liveTradingScope = "binance-futures-testnet",
            message = "Live trading is enabled for Binance Futures TESTNET only. Real funds are not at risk."
        })).AllowAnonymous();

        var auth = grp.RequireAuthorization();

        auth.MapGet("/", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bots = await db.Bots
                .Where(b => b.UserId == userId)
                .Include(b => b.Strategy)
                .Include(b => b.Symbol)
                .OrderByDescending(b => b.UpdatedAt)
                .ToListAsync(ct);
            var dtos = bots.Select(b => ToDto(b, b.Strategy?.Kind, b.Symbol?.Code ?? "")).ToList();
            return Results.Ok(dtos);
        });

        auth.MapPost("/", async (CreateBotRequest req, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var strat = await db.Strategies.FirstOrDefaultAsync(s => s.Id == req.StrategyId && s.UserId == userId, ct);
            if (strat is null) return Results.BadRequest(new { error = "strategy_not_found" });
            var symbol = await db.Symbols.FirstOrDefaultAsync(s => s.Code == req.SymbolCode.ToUpper(), ct);
            if (symbol is null) return Results.BadRequest(new { error = $"symbol_not_found: {req.SymbolCode}" });

            var executionMarket = ParseMarket(req.ExecutionMarket) ?? MarketKind.Spot;
            // v1: TriggerMarket = ExecutionMarket (cross-market hidden, scaffold only). Khi v2
            // mở UI advanced, accept req.TriggerMarket riêng + bắt buộc MaxBasisPct non-null.
            var triggerMarket = executionMarket;

            // Leverage > 1 chỉ hợp lệ khi ExecutionMarket=Futures. Spot mà set leverage = config sai.
            var leverage = req.Leverage is int lev ? Math.Clamp(lev, 1, 125) : 1;
            if (executionMarket == MarketKind.Spot && leverage > 1)
                return Results.BadRequest(new { error = "leverage_requires_futures", detail = "Set ExecutionMarket=Futures or Leverage=1." });

            // API key phải match ExecutionMarket. Paper trading hoặc unlinked key thì skip check.
            var resolvedKeyId = await ResolveApiKeyAsync(db, userId, req.ApiKeyId, ct);
            var apiKeyMarketError = await ValidateApiKeyMarketAsync(db, resolvedKeyId, executionMarket, ct);
            if (apiKeyMarketError is not null) return Results.BadRequest(new { error = apiKeyMarketError });

            var bot = new Bot
            {
                UserId = userId,
                StrategyId = strat.Id,
                SymbolId = symbol.Id,
                ExchangeId = symbol.ExchangeId,
                Name = req.Name,
                Mode = TradingMode.Paper,
                State = BotState.Stopped,
                RunMode = ParseRunMode(req.RunMode) ?? BotRunMode.PaperTrading,
                Kind = ParseKind(req.Kind) ?? BotKind.Signal,
                KindConfigJson = NormalizeJson(req.KindConfigJson),
                ApiKeyId = resolvedKeyId,
                Leverage = leverage,
                ExecutionMarket = executionMarket,
                TriggerMarket = triggerMarket,
                ContextFiltersJson = NormalizeJson(req.ContextFiltersJson),
                MaxBasisPct = req.MaxBasisPct ?? 0.5m,
                SymbolFilterJson = req.SymbolFilterJson,
                BaseEquityUsdt = req.BaseEquityUsdt ?? 1000m,
                MaxPositionSize = req.MaxPositionSize,
                RiskPerTradePercent = req.RiskPerTradePercent,
                DailyLossStopPercent = req.DailyLossStopPercent ?? 4m,
                MaxOpenPositions = req.MaxOpenPositions ?? 1,
                MaxConsecutiveLosses = req.MaxConsecutiveLosses ?? 0,
                CooldownAfterLossMinutes = req.CooldownAfterLossMinutes ?? 0,
                KillSwitchEnabled = req.KillSwitchEnabled ?? true,
                StopLossKind = ParseStopLossKind(req.StopLossKind),
                DefaultStopLossPercent = req.DefaultStopLossPercent,
                AtrPeriod = req.AtrPeriod ?? 14,
                AtrMultiplier = req.AtrMultiplier ?? 1.5m,
                DefaultTakeProfitPercent = req.DefaultTakeProfitPercent,
                TakeProfitLevelsJson = SerializeTpLevels(req.TakeProfitLevels),
                DefaultTrailingStopPercent = req.DefaultTrailingStopPercent,
                BreakEvenEnabled = req.BreakEvenEnabled ?? false,
                BreakEvenTriggerPercent = req.BreakEvenTriggerPercent,
                BreakEvenOffsetPercent = req.BreakEvenOffsetPercent ?? 0.1m,
            };
            db.Bots.Add(bot);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(bot, strat.Kind, symbol.Code));
        });

        auth.MapPatch("/{id:guid}/risk", async (Guid id, UpdateBotRiskRequest req, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.Include(b => b.Strategy).Include(b => b.Symbol)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();
            var rm = ParseRunMode(req.RunMode);
            if (rm.HasValue) bot.RunMode = rm.Value;
            var k = ParseKind(req.Kind);
            if (k.HasValue) bot.Kind = k.Value;
            if (req.KindConfigJson is not null) bot.KindConfigJson = NormalizeJson(req.KindConfigJson);
            // ExecutionMarket update trước: leverage/api-key validation đều phụ thuộc vào nó.
            var newExecMarket = ParseMarket(req.ExecutionMarket);
            if (newExecMarket.HasValue)
            {
                bot.ExecutionMarket = newExecMarket.Value;
                bot.TriggerMarket = newExecMarket.Value; // v1 force Trigger=Execution
            }

            if (req.UnlinkApiKey == true) bot.ApiKeyId = null;
            else if (req.ApiKeyId.HasValue) bot.ApiKeyId = await ResolveApiKeyAsync(db, userId, req.ApiKeyId, ct);

            // Re-validate sau khi update market + api key. Cho phép người dùng đổi market hoặc key
            // đồng thời, nhưng final state phải nhất quán.
            var apiKeyMarketError = await ValidateApiKeyMarketAsync(db, bot.ApiKeyId, bot.ExecutionMarket, ct);
            if (apiKeyMarketError is not null) return Results.BadRequest(new { error = apiKeyMarketError });

            if (req.Leverage is int lev)
            {
                if (bot.ExecutionMarket == MarketKind.Spot && lev > 1)
                    return Results.BadRequest(new { error = "leverage_requires_futures" });
                bot.Leverage = Math.Clamp(lev, 1, 125);
            }
            if (req.ContextFiltersJson is not null)
                bot.ContextFiltersJson = NormalizeJson(req.ContextFiltersJson);
            if (req.MaxBasisPct.HasValue) bot.MaxBasisPct = req.MaxBasisPct.Value;
            if (req.SymbolFilterJson is not null) bot.SymbolFilterJson = string.IsNullOrWhiteSpace(req.SymbolFilterJson) ? null : req.SymbolFilterJson;
            if (req.BaseEquityUsdt.HasValue) bot.BaseEquityUsdt = req.BaseEquityUsdt.Value;
            if (req.RiskPerTradePercent.HasValue) bot.RiskPerTradePercent = req.RiskPerTradePercent.Value;
            if (req.DailyLossStopPercent.HasValue) bot.DailyLossStopPercent = req.DailyLossStopPercent.Value;
            if (req.MaxOpenPositions.HasValue) bot.MaxOpenPositions = req.MaxOpenPositions.Value;
            if (req.MaxConsecutiveLosses.HasValue) bot.MaxConsecutiveLosses = req.MaxConsecutiveLosses.Value;
            if (req.CooldownAfterLossMinutes.HasValue) bot.CooldownAfterLossMinutes = req.CooldownAfterLossMinutes.Value;
            if (req.KillSwitchEnabled.HasValue) bot.KillSwitchEnabled = req.KillSwitchEnabled.Value;
            if (!string.IsNullOrWhiteSpace(req.StopLossKind)) bot.StopLossKind = ParseStopLossKind(req.StopLossKind);
            if (req.DefaultStopLossPercent.HasValue) bot.DefaultStopLossPercent = req.DefaultStopLossPercent.Value;
            if (req.AtrPeriod.HasValue) bot.AtrPeriod = req.AtrPeriod.Value;
            if (req.AtrMultiplier.HasValue) bot.AtrMultiplier = req.AtrMultiplier.Value;
            if (req.DefaultTakeProfitPercent.HasValue) bot.DefaultTakeProfitPercent = req.DefaultTakeProfitPercent.Value;
            if (req.TakeProfitLevels is not null) bot.TakeProfitLevelsJson = SerializeTpLevels(req.TakeProfitLevels);
            if (req.DefaultTrailingStopPercent.HasValue) bot.DefaultTrailingStopPercent = req.DefaultTrailingStopPercent.Value;
            if (req.BreakEvenEnabled.HasValue) bot.BreakEvenEnabled = req.BreakEvenEnabled.Value;
            if (req.BreakEvenTriggerPercent.HasValue) bot.BreakEvenTriggerPercent = req.BreakEvenTriggerPercent.Value;
            if (req.BreakEvenOffsetPercent.HasValue) bot.BreakEvenOffsetPercent = req.BreakEvenOffsetPercent.Value;
            bot.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(bot, bot.Strategy?.Kind, bot.Symbol?.Code ?? ""));
        });

        auth.MapPost("/{id:guid}/kill-switch", async (Guid id, TripKillSwitchRequest req, IRiskEngine engine, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var owned = await db.Bots.AnyAsync(b => b.Id == id && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();
            await engine.TripKillSwitchAsync(id, string.IsNullOrWhiteSpace(req.Reason) ? "manual" : req.Reason, ct);
            return Results.Ok(new { tripped = true });
        });

        auth.MapPost("/{id:guid}/kill-switch/reset", async (Guid id, IRiskEngine engine, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var owned = await db.Bots.AnyAsync(b => b.Id == id && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();
            await engine.ResetKillSwitchAsync(id, ct);
            return Results.Ok(new { tripped = false });
        });

        auth.MapGet("/{id:guid}/risk-events", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var owned = await db.Bots.AnyAsync(b => b.Id == id && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();
            var events = await db.RiskEvents
                .Where(e => e.BotId == id)
                .OrderByDescending(e => e.CreatedAt)
                .Take(100)
                .Select(e => new RiskEventDto(e.Id, e.BotId, e.EventType, e.Severity, e.Message, e.ActionTaken, e.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(events);
        });

        auth.MapPost("/{id:guid}/start", async (Guid id, QuantFlowBotsDbContext db, BotRuntime runtime, IBotEventBus bus, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots
                .Include(b => b.Strategy)
                .Include(b => b.Symbol)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();
            bot.State = BotState.Running;
            bot.LastError = null;
            bot.UpdatedAt = DateTimeOffset.UtcNow;
            db.BotRuns.Add(new BotRun { BotId = bot.Id, StartedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            runtime.Activate(bot);
            await bus.PublishAsync(new BotEvent(bot.Id, "started", $"bot {bot.Name} started", DateTimeOffset.UtcNow), ct);
            return Results.Ok(new { state = bot.State.ToString() });
        });

        auth.MapPost("/{id:guid}/stop", async (Guid id, QuantFlowBotsDbContext db, BotRuntime runtime, IBotEventBus bus, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();
            bot.State = BotState.Stopped;
            bot.UpdatedAt = DateTimeOffset.UtcNow;
            var run = await db.BotRuns.Where(r => r.BotId == bot.Id && r.StoppedAt == null).OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
            if (run is not null) { run.StoppedAt = DateTimeOffset.UtcNow; run.StopReason = "user_stop"; }
            await db.SaveChangesAsync(ct);
            runtime.Deactivate(bot.Id);
            await bus.PublishAsync(new BotEvent(bot.Id, "stopped", $"bot {bot.Name} stopped", DateTimeOffset.UtcNow), ct);
            return Results.Ok(new { state = bot.State.ToString() });
        });

        auth.MapGet("/{id:guid}/orders", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var owned = await db.Bots.AnyAsync(b => b.Id == id && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();
            var orders = await db.Orders.Where(o => o.BotId == id)
                .OrderByDescending(o => o.CreatedAt).Take(200)
                .Select(o => new OrderDto(o.Id, o.Side.ToString(), o.Status.ToString(), o.AveragePrice, o.FilledQuantity, o.Commission,
                    0m, o.CreatedAt)).ToListAsync(ct);
            return Results.Ok(orders);
        });

        auth.MapGet("/{id:guid}/positions", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var owned = await db.Bots.AnyAsync(b => b.Id == id && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();
            var positions = await db.Positions.Where(p => p.BotId == id)
                .OrderByDescending(p => p.OpenedAt).Take(50)
                .Select(p => new PositionDto(p.Id, p.Side.ToString(), p.Status.ToString(), p.Quantity, p.OriginalQuantity, p.EntryPrice, p.ExitPrice,
                    p.StopLossPrice, p.TakeProfitPrice, p.TrailingStopPercent,
                    p.TakeProfitLevelsJson, p.BreakEvenTriggered,
                    p.RealizedPnl, p.CloseReason, p.OpenedAt, p.ClosedAt))
                .ToListAsync(ct);
            return Results.Ok(positions);
        });

        // Cross-bot summary: one row per bot, sortable on FE. Cheap query — pulls closed positions then aggregates in memory.
        auth.MapGet("/stats/summary", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bots = await db.Bots
                .Where(b => b.UserId == userId)
                .Include(b => b.Symbol)
                .ToListAsync(ct);
            if (bots.Count == 0) return Results.Ok(Array.Empty<BotStatsRowDto>());
            var botIds = bots.Select(b => b.Id).ToList();
            var positions = await db.Positions
                .Where(p => botIds.Contains(p.BotId))
                .Select(p => new { p.BotId, p.Status, p.RealizedPnl, p.ClosedAt, p.OpenedAt })
                .ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var rows = new List<BotStatsRowDto>(bots.Count);
            foreach (var b in bots)
            {
                var mine = positions.Where(p => p.BotId == b.Id).ToList();
                var closed = mine.Where(p => p.Status == PositionStatus.Closed).OrderBy(p => p.ClosedAt).ToList();
                var realized = closed.Sum(p => p.RealizedPnl);
                var wins = closed.Count(p => p.RealizedPnl > 0);
                var maxDd = ComputeMaxDrawdownPct(b.BaseEquityUsdt, closed.Select(p => p.RealizedPnl));
                rows.Add(new BotStatsRowDto(
                    b.Id, b.Name, b.Symbol?.Code ?? "", b.RunMode.ToString(), b.State.ToString(),
                    b.BaseEquityUsdt, b.BaseEquityUsdt + realized, realized,
                    b.BaseEquityUsdt > 0 ? realized / b.BaseEquityUsdt * 100m : 0m,
                    closed.Count,
                    closed.Count > 0 ? (decimal)wins / closed.Count * 100m : 0m,
                    maxDd,
                    closed.Where(p => p.ClosedAt >= now.AddHours(-24)).Sum(p => p.RealizedPnl),
                    closed.Where(p => p.ClosedAt >= now.AddDays(-7)).Sum(p => p.RealizedPnl),
                    mine.Count(p => p.Status == PositionStatus.Open)));
            }
            return Results.Ok(rows.OrderByDescending(r => r.TotalRealizedPnl).ToList());
        });

        auth.MapGet("/{id:guid}/stats", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.Include(b => b.Symbol).FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();

            var positions = await db.Positions
                .Where(p => p.BotId == id)
                .OrderBy(p => p.OpenedAt)
                .ToListAsync(ct);
            var closed = positions.Where(p => p.Status == PositionStatus.Closed).OrderBy(p => p.ClosedAt).ToList();
            var open = positions.Where(p => p.Status == PositionStatus.Open).ToList();

            // Unrealized for open positions: use latest candle close for the symbol+1m.
            decimal unrealized = 0m;
            if (open.Count > 0)
            {
                var lastCandle = await db.Candles
                    .Where(c => c.SymbolId == bot.SymbolId && c.Interval == CandleInterval.OneMinute)
                    .OrderByDescending(c => c.OpenTime)
                    .FirstOrDefaultAsync(ct);
                if (lastCandle is not null)
                {
                    foreach (var op in open)
                        unrealized += (lastCandle.Close - op.EntryPrice) * op.Quantity;
                }
            }

            var wins = closed.Where(p => p.RealizedPnl > 0).ToList();
            var losses = closed.Where(p => p.RealizedPnl < 0).ToList();
            var realized = closed.Sum(p => p.RealizedPnl);
            var grossWin = wins.Sum(p => p.RealizedPnl);
            var grossLoss = Math.Abs(losses.Sum(p => p.RealizedPnl));
            var winRate = closed.Count > 0 ? (decimal)wins.Count / closed.Count * 100m : 0m;
            var avgWin = wins.Count > 0 ? wins.Average(p => p.RealizedPnl) : 0m;
            var avgLoss = losses.Count > 0 ? losses.Average(p => p.RealizedPnl) : 0m;
            var profitFactor = grossLoss > 0m ? grossWin / grossLoss : (grossWin > 0m ? 999m : 0m);
            var expectancy = closed.Count > 0 ? realized / closed.Count : 0m;
            var largestWin = wins.Count > 0 ? wins.Max(p => p.RealizedPnl) : 0m;
            var largestLoss = losses.Count > 0 ? losses.Min(p => p.RealizedPnl) : 0m;

            // Equity curve from base equity, accumulating each closed PnL.
            decimal equity = bot.BaseEquityUsdt;
            var curve = new List<EquityPoint> { new(bot.CreatedAt, equity) };
            decimal peak = equity, maxDd = 0m;
            foreach (var p in closed)
            {
                equity += p.RealizedPnl;
                curve.Add(new EquityPoint(p.ClosedAt ?? DateTimeOffset.UtcNow, equity));
                if (equity > peak) peak = equity;
                var dd = peak > 0 ? (peak - equity) / peak * 100m : 0m;
                if (dd > maxDd) maxDd = dd;
            }

            var now = DateTimeOffset.UtcNow;
            var pnlToday = closed.Where(p => p.ClosedAt >= now.AddHours(-24)).Sum(p => p.RealizedPnl);
            var pnl7d = closed.Where(p => p.ClosedAt >= now.AddDays(-7)).Sum(p => p.RealizedPnl);
            var pnl30d = closed.Where(p => p.ClosedAt >= now.AddDays(-30)).Sum(p => p.RealizedPnl);
            var tradesToday = closed.Count(p => p.ClosedAt >= now.AddHours(-24));

            var dto = new BotStatsDto(
                bot.Id, bot.Name, bot.BaseEquityUsdt, bot.BaseEquityUsdt + realized + unrealized,
                realized, unrealized,
                bot.BaseEquityUsdt > 0 ? (realized + unrealized) / bot.BaseEquityUsdt * 100m : 0m,
                closed.Count, wins.Count, losses.Count, open.Count,
                winRate, avgWin, avgLoss, largestWin, largestLoss,
                profitFactor, maxDd, expectancy,
                pnlToday, pnl7d, pnl30d, tradesToday,
                closed.FirstOrDefault()?.OpenedAt,
                closed.LastOrDefault()?.ClosedAt,
                curve);
            return Results.Ok(dto);
        });

        auth.MapGet("/{id:guid}/signals", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();
            var signals = await db.Signals.Where(s => s.StrategyId == bot.StrategyId)
                .OrderByDescending(s => s.GeneratedAt).Take(100)
                .Select(s => new SignalDto(s.Id, s.Type.ToString(), s.Side != null ? s.Side.ToString() : null, s.Price, s.Score, s.PayloadJson, s.GeneratedAt))
                .ToListAsync(ct);
            return Results.Ok(signals);
        });

        auth.MapGet("/{id:guid}/live-position", async (
            Guid id,
            QuantFlowBotsDbContext db,
            IApiKeyEncryption encryption,
            BinanceFuturesRestClient futures,
            BinanceSpotSignedClient spotClient,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.Include(b => b.Symbol)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot?.Symbol is null) return Results.NotFound();
            if (bot.ApiKeyId is null) return Results.Ok(new { error = "no_api_key_linked" });

            var key = await db.ApiKeys.Include(k => k.Exchange).FirstOrDefaultAsync(k => k.Id == bot.ApiKeyId, ct);
            if (key?.Exchange is null) return Results.Ok(new { error = "api_key_missing" });
            try
            {
                if (key.Exchange.Code == "binance-spot-testnet")
                {
                    var cred = new SpotCredential(encryption.Decrypt(key.EncryptedKey),
                        encryption.Decrypt(key.EncryptedSecret), key.Exchange.RestBaseUrl);
                    var acct = await spotClient.GetAccountAsync(cred, ct);
                    var baseBal = acct.Balances.FirstOrDefault(b => b.Asset == bot.Symbol.BaseAsset);
                    var quoteBal = acct.Balances.FirstOrDefault(b => b.Asset == bot.Symbol.QuoteAsset);
                    return Results.Ok(new
                    {
                        kind = "spot",
                        canTrade = acct.CanTrade,
                        canWithdraw = acct.CanWithdraw,
                        baseAsset = bot.Symbol.BaseAsset,
                        baseFree = baseBal?.Free ?? 0m,
                        baseLocked = baseBal?.Locked ?? 0m,
                        quoteAsset = bot.Symbol.QuoteAsset,
                        quoteFree = quoteBal?.Free ?? 0m,
                        quoteLocked = quoteBal?.Locked ?? 0m,
                    });
                }
                var futCred = new FuturesCredential(encryption.Decrypt(key.EncryptedKey),
                    encryption.Decrypt(key.EncryptedSecret), key.Exchange.RestBaseUrl);
                var snap = await futures.GetPositionRiskAsync(futCred, bot.Symbol.Code, ct);
                return Results.Ok(snap);
            }
            catch (Exception ex) { return Results.Ok(new { error = ex.Message }); }
        });

        auth.MapPost("/{id:guid}/live-smoke-test", async (
            Guid id,
            QuantFlowBotsDbContext db,
            ILiveTradingGate gate,
            IApiKeyEncryption encryption,
            BinanceFuturesRestClient futures,
            BinanceSpotSignedClient spotClient,
            FuturesSymbolFiltersCache filters,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.Include(b => b.Symbol)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot?.Symbol is null) return Results.NotFound();

            var check = await gate.EvaluateAsync(id, ct);
            if (!check.Allowed) return Results.BadRequest(new { error = check.Reason });

            var key = await db.ApiKeys.Include(k => k.Exchange).FirstAsync(k => k.Id == bot.ApiKeyId, ct);

            if (key.Exchange!.Code == "binance-spot-testnet")
            {
                var cred = new SpotCredential(encryption.Decrypt(key.EncryptedKey),
                    encryption.Decrypt(key.EncryptedSecret), key.Exchange.RestBaseUrl);
                var minQty = bot.Symbol.MinQuantity > 0 ? bot.Symbol.MinQuantity : 0.001m;
                var coid = $"qfb_test_{Guid.NewGuid():N}"[..32];
                SpotOrderResult r;
                try { r = await spotClient.PlaceMarketOrderAsync(cred, bot.Symbol.Code, OrderSide.Buy, minQty, coid, ct); }
                catch (Exception ex) { return Results.Ok(new { stage = "place", error = ex.Message }); }
                if (r.Status != OrderStatus.Filled && r.Status != OrderStatus.PartiallyFilled)
                    return Results.Ok(new { stage = "place", status = r.Status.ToString(), reject = r.RejectReason });
                var closeCoid = $"qfb_test_x_{Guid.NewGuid():N}"[..32];
                SpotOrderResult closeR;
                try { closeR = await spotClient.PlaceMarketOrderAsync(cred, bot.Symbol.Code, OrderSide.Sell, r.FilledQuantity, closeCoid, ct); }
                catch (Exception ex) { return Results.Ok(new { stage = "close", entryStatus = r.Status.ToString(), error = ex.Message }); }
                return Results.Ok(new
                {
                    ok = true,
                    kind = "spot",
                    entry = new { r.ExchangeOrderId, r.AveragePrice, r.FilledQuantity, status = r.Status.ToString() },
                    exit = new { closeR.ExchangeOrderId, closeR.AveragePrice, closeR.FilledQuantity, status = closeR.Status.ToString() },
                    note = "Both spot orders placed against testnet; verify on testnet.binance.vision."
                });
            }

            // Futures path
            var fcred = new FuturesCredential(encryption.Decrypt(key.EncryptedKey),
                encryption.Decrypt(key.EncryptedSecret), key.Exchange.RestBaseUrl);
            var filter = await filters.GetAsync(fcred.BaseUrl!, bot.Symbol.Code, ct);
            if (filter is null) return Results.BadRequest(new { error = "symbol_not_listed_on_futures" });
            var fMinQty = filter.MinQuantity;
            var fcoid = $"qfb_test_{Guid.NewGuid():N}"[..32];
            FuturesOrderResult fr;
            try { fr = await futures.PlaceMarketOrderAsync(fcred, bot.Symbol.Code, OrderSide.Buy, fMinQty, fcoid, ct); }
            catch (Exception ex) { return Results.Ok(new { stage = "place", error = ex.Message }); }
            if (fr.Status != OrderStatus.Filled && fr.Status != OrderStatus.PartiallyFilled)
                return Results.Ok(new { stage = "place", status = fr.Status.ToString(), reject = fr.RejectReason });
            var fcloseCoid = $"qfb_test_x_{Guid.NewGuid():N}"[..32];
            FuturesOrderResult fcloseR;
            try { fcloseR = await futures.PlaceMarketOrderAsync(fcred, bot.Symbol.Code, OrderSide.Sell, fr.FilledQuantity, fcloseCoid, ct); }
            catch (Exception ex) { return Results.Ok(new { stage = "close", entryStatus = fr.Status.ToString(), error = ex.Message }); }
            return Results.Ok(new
            {
                ok = true,
                kind = "futures",
                entry = new { fr.ExchangeOrderId, fr.AveragePrice, fr.FilledQuantity, status = fr.Status.ToString() },
                exit = new { fcloseR.ExchangeOrderId, fcloseR.AveragePrice, fcloseR.FilledQuantity, status = fcloseR.Status.ToString() },
                note = "Both futures orders placed against testnet; verify on testnet.binancefuture.com."
            });
        });

        auth.MapDelete("/{id:guid}", async (Guid id, QuantFlowBotsDbContext db, BotRuntime runtime, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();
            runtime.Deactivate(bot.Id);
            db.Bots.Remove(bot);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static BotDto ToDto(Bot b, string? strategyKind, string symbolCode)
        => new(b.Id, b.Name, b.Mode.ToString(), b.State.ToString(), b.Kind.ToString(), b.KindConfigJson, b.ApiKeyId, b.Leverage, b.RunMode.ToString(), b.SymbolFilterJson,
            b.ExecutionMarket.ToString(), b.TriggerMarket.ToString(), b.ContextFiltersJson, b.MaxBasisPct,
            b.StrategyId, strategyKind,
            b.SymbolId, symbolCode,
            b.BaseEquityUsdt, b.MaxPositionSize, b.RiskPerTradePercent,
            b.DailyLossStopPercent, b.MaxOpenPositions, b.MaxConsecutiveLosses, b.CooldownAfterLossMinutes,
            b.KillSwitchEnabled, b.KillSwitchTrippedAt, b.KillSwitchReason,
            b.StopLossKind.ToString(), b.DefaultStopLossPercent, b.AtrPeriod, b.AtrMultiplier,
            b.DefaultTakeProfitPercent, b.TakeProfitLevelsJson, b.DefaultTrailingStopPercent,
            b.BreakEvenEnabled, b.BreakEvenTriggerPercent, b.BreakEvenOffsetPercent,
            b.LastError, b.CreatedAt);

    private static MarketKind? ParseMarket(string? raw)
        => Enum.TryParse<MarketKind>(raw, true, out var m) ? m : (MarketKind?)null;

    // Bot có ApiKey thì exchange code của key phải khớp ExecutionMarket. Paper trading hoặc
    // unlinked key: skip (paper executor không cần real key). binance-futures-testnet ⇄ Futures,
    // binance-spot-testnet ⇄ Spot. Mã exchange khác (vd seed/dev) → bỏ qua (không gắt) để
    // không khoá scenario thử nghiệm.
    private static async Task<string?> ValidateApiKeyMarketAsync(QuantFlowBotsDbContext db, Guid? apiKeyId, MarketKind executionMarket, CancellationToken ct)
    {
        if (apiKeyId is null) return null;
        var code = await db.ApiKeys.Where(k => k.Id == apiKeyId.Value)
            .Select(k => k.Exchange!.Code).FirstOrDefaultAsync(ct);
        return code switch
        {
            "binance-futures-testnet" when executionMarket != MarketKind.Futures
                => "api_key_market_mismatch: futures key linked but ExecutionMarket=Spot",
            "binance-spot-testnet" when executionMarket != MarketKind.Spot
                => "api_key_market_mismatch: spot key linked but ExecutionMarket=Futures",
            _ => null,
        };
    }

    private static StopLossKind ParseStopLossKind(string? raw)
        => Enum.TryParse<StopLossKind>(raw, true, out var k) ? k : StopLossKind.FixedPercent;

    private static BotRunMode? ParseRunMode(string? raw)
        => Enum.TryParse<BotRunMode>(raw, true, out var k) ? k : (BotRunMode?)null;

    private static decimal ComputeMaxDrawdownPct(decimal baseEquity, IEnumerable<decimal> realizedSequence)
    {
        decimal equity = baseEquity, peak = baseEquity, maxDd = 0m;
        foreach (var pnl in realizedSequence)
        {
            equity += pnl;
            if (equity > peak) peak = equity;
            var dd = peak > 0 ? (peak - equity) / peak * 100m : 0m;
            if (dd > maxDd) maxDd = dd;
        }
        return maxDd;
    }

    private static async Task<Guid?> ResolveApiKeyAsync(QuantFlowBotsDbContext db, Guid userId, Guid? apiKeyId, CancellationToken ct)
    {
        if (apiKeyId is null) return null;
        var owned = await db.ApiKeys.AnyAsync(k => k.Id == apiKeyId.Value && k.UserId == userId, ct);
        return owned ? apiKeyId : null;
    }

    private static BotKind? ParseKind(string? raw)
        => Enum.TryParse<BotKind>(raw, true, out var k) ? k : (BotKind?)null;

    private static string? NormalizeJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { System.Text.Json.JsonDocument.Parse(raw); return raw; }
        catch { return null; }
    }

    private static string? SerializeTpLevels(IReadOnlyList<TpLevelInput>? levels)
    {
        if (levels is null || levels.Count == 0) return null;
        var cleaned = levels
            .Where(l => l.ProfitPercent > 0 && l.ClosePercent > 0)
            .OrderBy(l => l.ProfitPercent)
            .Select(l => new { profitPercent = l.ProfitPercent, closePercent = l.ClosePercent })
            .ToList();
        if (cleaned.Count == 0) return null;
        return System.Text.Json.JsonSerializer.Serialize(cleaned);
    }

    private static Guid ParseUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
}
