using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Application.Backtesting;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Api.Endpoints;

public static class BacktestEndpoints
{
    public sealed record RunRequest(
        Guid StrategyId,
        string SymbolCode,
        string Interval,
        DateTimeOffset From,
        DateTimeOffset To,
        decimal? InitialCapital,
        decimal? CommissionPercent,
        string? ParametersJson);

    public sealed record BacktestSummaryDto(
        Guid Id,
        Guid StrategyId,
        string StrategyKind,
        string SymbolCode,
        string Interval,
        DateTimeOffset FromTime,
        DateTimeOffset ToTime,
        decimal InitialCapital,
        string Status,
        decimal? FinalEquity,
        decimal? ReturnPercent,
        decimal? MaxDrawdownPercent,
        decimal? SharpeRatio,
        int? TradeCount,
        decimal? WinRatePercent,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        string? Error);

    public sealed record BacktestDetailDto(BacktestSummaryDto Summary, IReadOnlyList<EquityPoint> EquityCurve);

    public static IEndpointRouteBuilder MapBacktests(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/backtests").WithTags("backtests").RequireAuthorization();

        grp.MapGet("/", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var list = await db.Backtests
                .Where(b => b.UserId == userId)
                .Include(b => b.Strategy)
                .Include(b => b.Symbol)
                .OrderByDescending(b => b.CreatedAt)
                .Take(100)
                .ToListAsync(ct);
            return Results.Ok(list.Select(b => ToSummary(b)));
        });

        grp.MapGet("/{id:guid}", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var b = await db.Backtests
                .Include(x => x.Strategy)
                .Include(x => x.Symbol)
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (b is null) return Results.NotFound();
            var curve = Array.Empty<EquityPoint>() as IReadOnlyList<EquityPoint>;
            if (!string.IsNullOrEmpty(b.ResultJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(b.ResultJson);
                    if (doc.RootElement.TryGetProperty("equityCurve", out var ec))
                    {
                        var list = new List<EquityPoint>();
                        foreach (var p in ec.EnumerateArray())
                            list.Add(new EquityPoint(p.GetProperty("at").GetDateTimeOffset(), p.GetProperty("equity").GetDecimal()));
                        curve = list;
                    }
                }
                catch { }
            }
            return Results.Ok(new BacktestDetailDto(ToSummary(b), curve));
        });

        grp.MapPost("/", async (RunRequest req, QuantFlowBotsDbContext db, IBacktestRunner runner, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var strat = await db.Strategies.FirstOrDefaultAsync(s => s.Id == req.StrategyId && s.UserId == userId, ct);
            if (strat is null) return Results.BadRequest(new { error = "strategy_not_found" });
            var symbol = await db.Symbols.FirstOrDefaultAsync(s => s.Code == req.SymbolCode.ToUpper(), ct);
            if (symbol is null) return Results.BadRequest(new { error = $"symbol_not_found: {req.SymbolCode}" });
            if (!Enum.TryParse<CandleInterval>(req.Interval, true, out var interval))
                return Results.BadRequest(new { error = $"unknown_interval: {req.Interval}" });
            if (req.From >= req.To) return Results.BadRequest(new { error = "from_must_be_before_to" });

            var backtest = new Backtest
            {
                UserId = userId,
                StrategyId = strat.Id,
                SymbolId = symbol.Id,
                Interval = interval,
                FromTime = req.From,
                ToTime = req.To,
                InitialCapital = req.InitialCapital ?? 10_000m,
                CommissionPercent = req.CommissionPercent ?? 0.1m,
                ParametersJson = req.ParametersJson ?? strat.ParametersJson,
                Status = BacktestStatus.Running,
            };
            db.Backtests.Add(backtest);
            await db.SaveChangesAsync(ct);

            try
            {
                var metrics = await runner.RunAsync(new BacktestRequest(
                    backtest.Id, backtest.StrategyId, backtest.SymbolId, backtest.Interval,
                    backtest.FromTime, backtest.ToTime,
                    backtest.InitialCapital, backtest.CommissionPercent, backtest.ParametersJson), ct);

                backtest.Status = BacktestStatus.Completed;
                backtest.ResultJson = JsonSerializer.Serialize(new
                {
                    finalEquity = metrics.FinalEquity,
                    totalReturnPercent = metrics.TotalReturnPercent,
                    maxDrawdownPercent = metrics.MaxDrawdownPercent,
                    sharpeRatio = metrics.SharpeRatio,
                    tradeCount = metrics.TradeCount,
                    winCount = metrics.WinCount,
                    winRatePercent = metrics.WinRatePercent,
                    equityCurve = metrics.EquityCurve.Select(p => new { at = p.At, equity = p.Equity }),
                });
                backtest.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                await db.Entry(backtest).Reference(b => b.Strategy).LoadAsync(ct);
                await db.Entry(backtest).Reference(b => b.Symbol).LoadAsync(ct);
                return Results.Ok(new BacktestDetailDto(ToSummary(backtest), metrics.EquityCurve));
            }
            catch (Exception ex)
            {
                backtest.Status = BacktestStatus.Failed;
                backtest.ErrorMessage = ex.Message;
                backtest.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }

    private static BacktestSummaryDto ToSummary(Backtest b)
    {
        decimal? finalEquity = null, ret = null, dd = null, sharpe = null, winRate = null;
        int? trades = null;
        if (!string.IsNullOrEmpty(b.ResultJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(b.ResultJson);
                var r = doc.RootElement;
                if (r.TryGetProperty("finalEquity", out var v)) finalEquity = v.GetDecimal();
                if (r.TryGetProperty("totalReturnPercent", out v)) ret = v.GetDecimal();
                if (r.TryGetProperty("maxDrawdownPercent", out v)) dd = v.GetDecimal();
                if (r.TryGetProperty("sharpeRatio", out v)) sharpe = v.GetDecimal();
                if (r.TryGetProperty("tradeCount", out v)) trades = v.GetInt32();
                if (r.TryGetProperty("winRatePercent", out v)) winRate = v.GetDecimal();
            }
            catch { }
        }
        return new BacktestSummaryDto(b.Id, b.StrategyId, b.Strategy?.Kind ?? "", b.Symbol?.Code ?? "",
            b.Interval.ToString(), b.FromTime, b.ToTime, b.InitialCapital, b.Status.ToString(),
            finalEquity, ret, dd, sharpe, trades, winRate, b.CreatedAt, b.CompletedAt, b.ErrorMessage);
    }

    private static Guid ParseUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
}
