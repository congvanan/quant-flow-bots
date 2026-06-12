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
        string? ParametersJson,
        // "Spot" (default) | "Futures". Futures: nến fapi, long+short, margin model + leverage.
        string? Market,
        decimal? Leverage);

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

    public sealed record ScanRequest(
        Guid StrategyId,
        string Interval,
        DateTimeOffset From,
        DateTimeOffset To,
        decimal? InitialCapital,
        decimal? CommissionPercent,
        string? ParametersJson,
        string? Market,
        decimal? Leverage,
        // Universe: top N USDT pairs theo 24h quote volume (5–100). MinQuoteVolume lọc thêm đáy.
        int? TopN,
        decimal? MinQuoteVolume);

    public sealed record ScanRowDto(
        string Symbol,
        bool Ok,
        string? Error,
        decimal? ReturnPercent,
        decimal? MaxDrawdownPercent,
        decimal? SharpeRatio,
        int? TradeCount,
        decimal? WinRatePercent,
        decimal? FinalEquity);

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
            var market = MarketKind.Spot;
            if (!string.IsNullOrWhiteSpace(req.Market) && !Enum.TryParse(req.Market, true, out market))
                return Results.BadRequest(new { error = $"unknown_market: {req.Market} (Spot | Futures)" });
            var leverage = Math.Clamp(req.Leverage ?? 1m, 1m, 125m);
            if (market == MarketKind.Spot && leverage > 1m)
                return Results.BadRequest(new { error = "leverage_requires_futures" });

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
                    backtest.InitialCapital, backtest.CommissionPercent, backtest.ParametersJson,
                    market, leverage), ct);

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

        // Batch scan: chạy backtest cùng 1 strategy trên TOP-N coins theo 24h volume,
        // trả bảng so sánh per-symbol (không persist từng row — tránh spam Recent list;
        // muốn lưu + xem equity curve thì re-run single backtest cho symbol đó).
        // Đây cũng là blueprint cho live bot multi-symbol sau này (Bot.SymbolFilterJson).
        grp.MapPost("/scan", async (
            ScanRequest req,
            QuantFlowBotsDbContext db,
            QuantFlowBots.Infrastructure.Exchanges.Binance.TickerSnapshotCache tickerCache,
            IServiceScopeFactory scopeFactory,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var strat = await db.Strategies.FirstOrDefaultAsync(s => s.Id == req.StrategyId && s.UserId == userId, ct);
            if (strat is null) return Results.BadRequest(new { error = "strategy_not_found" });
            if (!Enum.TryParse<CandleInterval>(req.Interval, true, out var interval))
                return Results.BadRequest(new { error = $"unknown_interval: {req.Interval}" });
            if (req.From >= req.To) return Results.BadRequest(new { error = "from_must_be_before_to" });
            var market = MarketKind.Spot;
            if (!string.IsNullOrWhiteSpace(req.Market) && !Enum.TryParse(req.Market, true, out market))
                return Results.BadRequest(new { error = $"unknown_market: {req.Market}" });
            var leverage = Math.Clamp(req.Leverage ?? 1m, 1m, 125m);
            if (market == MarketKind.Spot && leverage > 1m)
                return Results.BadRequest(new { error = "leverage_requires_futures" });

            var topN = Math.Clamp(req.TopN ?? 50, 5, 100);
            var minVol = req.MinQuoteVolume ?? 0m;

            // Universe: top N USDT pairs theo 24h quote volume, loại stable/non-ASCII.
            var tickers = await tickerCache.GetAsync(ct);
            var universe = tickers
                .Where(t => t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && t.QuoteVolume > 0)
                .Where(t => t.Symbol.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '_')))
                .Where(t => !ScanStableBases.Contains(t.Symbol[..^4]))
                .Where(t => Math.Abs(t.LastPrice - 1m) > 0.03m)
                .Where(t => t.QuoteVolume >= minVol)
                .OrderByDescending(t => t.QuoteVolume)
                .Take(topN)
                .Select(t => t.Symbol.ToUpperInvariant())
                .ToList();
            if (universe.Count == 0) return Results.BadRequest(new { error = "universe_empty" });

            var symbolIds = await db.Symbols
                .Where(s => universe.Contains(s.Code))
                .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.OrdinalIgnoreCase, ct);

            var paramJson = req.ParametersJson ?? strat.ParametersJson;
            var capital = req.InitialCapital ?? 10_000m;
            var fee = req.CommissionPercent ?? 0.1m;

            // Scope per symbol: BacktestRunner + DbContext là scoped, KHÔNG share giữa các task
            // song song (DbContext không thread-safe). Concurrency 4 — mỗi backtest tự fetch
            // klines sequential, 4 luồng đủ ăn network mà không dồn ép Binance.
            var rows = new System.Collections.Concurrent.ConcurrentBag<ScanRowDto>();
            await Parallel.ForEachAsync(universe,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                async (sym, token) =>
                {
                    if (!symbolIds.TryGetValue(sym, out var symId))
                    {
                        rows.Add(new ScanRowDto(sym, false, "symbol_not_in_db", null, null, null, null, null, null));
                        return;
                    }
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var scopedRunner = scope.ServiceProvider.GetRequiredService<IBacktestRunner>();
                        var metrics = await scopedRunner.RunAsync(new BacktestRequest(
                            Guid.NewGuid(), strat.Id, symId, interval, req.From, req.To,
                            capital, fee, paramJson, market, leverage), token);
                        rows.Add(new ScanRowDto(sym, true, null,
                            Math.Round(metrics.TotalReturnPercent, 2),
                            Math.Round(metrics.MaxDrawdownPercent, 2),
                            Math.Round(metrics.SharpeRatio, 2),
                            metrics.TradeCount,
                            Math.Round(metrics.WinRatePercent, 1),
                            Math.Round(metrics.FinalEquity, 2)));
                    }
                    catch (Exception ex)
                    {
                        rows.Add(new ScanRowDto(sym, false, ex.Message, null, null, null, null, null, null));
                    }
                });

            var ok = rows.Where(r => r.Ok).OrderByDescending(r => r.ReturnPercent).ToList();
            var failed = rows.Where(r => !r.Ok).OrderBy(r => r.Symbol).ToList();
            return Results.Ok(new
            {
                scannedAt = DateTimeOffset.UtcNow,
                universe = universe.Count,
                okCount = ok.Count,
                failedCount = failed.Count,
                filter = new { topN, minQuoteVolume = minVol, market = market.ToString(), leverage, interval = req.Interval, from = req.From, to = req.To },
                results = ok,
                failures = failed,
            });
        });

        grp.MapDelete("/{id:guid}", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var b = await db.Backtests.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (b is null) return Results.NotFound();
            db.Backtests.Remove(b);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Bulk-delete tất cả backtests status=Failed của user — quick clear khi pollute UI.
        grp.MapDelete("/failed", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var failed = await db.Backtests
                .Where(b => b.UserId == userId && b.Status == BacktestStatus.Failed)
                .ToListAsync(ct);
            if (failed.Count == 0) return Results.Ok(new { deleted = 0 });
            db.Backtests.RemoveRange(failed);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = failed.Count });
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

    // Stable/wrapped-USD bases loại khỏi scan universe — volume to nhưng không trade được.
    // (Bản sao tối giản của MarketEndpoints.StableBaseAssets — list đó private.)
    private static readonly HashSet<string> ScanStableBases = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "FDUSD", "TUSD", "BUSD", "USDP", "USDD", "DAI", "SUSD", "USD1",
        "EUR", "EURI", "AEUR", "EURT", "PYUSD", "RLUSD", "UUSD", "USTC",
    };

    private static Guid ParseUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
}
