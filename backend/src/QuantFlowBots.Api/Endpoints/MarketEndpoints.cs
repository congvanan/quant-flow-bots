using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Trading;

namespace QuantFlowBots.Api.Endpoints;

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarket(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/market").WithTags("market");

        grp.MapGet("/overview", async (TickerSnapshotCache tickerCache, CancellationToken ct) =>
        {
            var tickers = await tickerCache.GetAsync(ct);
            var usdt = tickers.Where(t => t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && t.QuoteVolume > 0).ToList();
            return Results.Ok(new
            {
                updatedAt = DateTimeOffset.UtcNow,
                topGainers = usdt.OrderByDescending(t => t.PriceChangePercent).Take(10),
                topVolume = usdt.OrderByDescending(t => t.QuoteVolume).Take(10),
                sharpMovers = usdt.Where(t => Math.Abs(t.PriceChangePercent) >= 5)
                    .OrderByDescending(t => Math.Abs(t.PriceChangePercent)).Take(10)
            });
        });

        grp.MapGet("/new-listings", async (int? limit, TickerSnapshotCache tickerCache, QuantFlowBotsDbContext db, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 5, 1, 20);
            var newest = await db.Symbols
                .Where(s => s.IsActive && s.ListedAt != null && s.QuoteAsset == "USDT")
                .OrderByDescending(s => s.ListedAt)
                .Take(take * 4)
                .Select(s => new { s.Code, s.BaseAsset, s.ListedAt })
                .ToListAsync(ct);
            if (newest.Count == 0) return Results.Ok(Array.Empty<object>());

            var tickers = await tickerCache.GetAsync(ct);
            var byCode = tickers.ToDictionary(t => t.Symbol, StringComparer.OrdinalIgnoreCase);

            var result = newest
                .Select(s =>
                {
                    byCode.TryGetValue(s.Code, out var t);
                    return new
                    {
                        code = s.Code,
                        baseAsset = s.BaseAsset,
                        listedAt = s.ListedAt,
                        price = t?.LastPrice ?? 0m,
                        priceChangePercent = t?.PriceChangePercent ?? 0m,
                        quoteVolume = t?.QuoteVolume ?? 0m,
                    };
                })
                .Where(r => r.price > 0)
                .Take(take)
                .ToList();
            return Results.Ok(result);
        });

        grp.MapGet("/volume-spikes", (int? limit, VolumeSpikeCache cache) =>
        {
            var take = Math.Clamp(limit ?? 10, 1, 50);
            return Results.Ok(cache.Snapshot().Take(take));
        });

        grp.MapGet("/order-book-walls", (
            decimal? minNotional,
            decimal? maxDistancePct,
            string? side,
            int? limit,
            string? exclude,
            OrderBookWallCache cache,
            Microsoft.Extensions.Options.IOptions<OrderBookWallOptions> opts) =>
        {
            var min = minNotional ?? opts.Value.MinNotionalUsdt;
            var maxDist = maxDistancePct ?? opts.Value.MaxDistanceFromMidPercent;
            var take = Math.Clamp(limit ?? 25, 1, 200);
            var excludeSet = (exclude ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .ToHashSet();
            var excludedStableBases = opts.Value.ExcludedBaseAssets
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var quoteFilter = opts.Value.QuoteAssets is { Length: > 0 } ? opts.Value.QuoteAssets : ["USDT"];
            var stableBand = opts.Value.StablePairPriceBandPercent;
            var snap = cache.Snapshot();
            var filtered = snap
                .Where(w => w.QuoteNotional >= min)
                .Where(w => w.DistanceFromMidPercent <= maxDist)
                .Where(w => string.IsNullOrEmpty(side) || string.Equals(w.Side, side, StringComparison.OrdinalIgnoreCase))
                .Where(w => !excludeSet.Contains(w.Symbol))
                .Where(w => !IsExcludedStablePair(w.Symbol, quoteFilter, excludedStableBases))
                // Dynamic stable-pair filter as safety net for entries already in the cache
                // before the worker picked up the new options.
                .Where(w => stableBand <= 0m || Math.Abs(w.MidPrice - 1m) / 1m * 100m > stableBand)
                .OrderByDescending(w => w.QuoteNotional)
                .Take(take)
                .ToList();
            return Results.Ok(new
            {
                updatedAt = DateTimeOffset.UtcNow,
                filter = new { minNotional = min, maxDistancePct = maxDist, side = side ?? "any", limit = take },
                defaults = new { minNotional = opts.Value.MinNotionalUsdt, detectionFloor = opts.Value.DetectionFloorUsdt, maxDistancePct = opts.Value.MaxDistanceFromMidPercent, scanIntervalSeconds = opts.Value.ScanIntervalSeconds, maxSymbols = opts.Value.MaxSymbols, excludedBaseAssets = opts.Value.ExcludedBaseAssets },
                totalCached = snap.Count,
                count = filtered.Count,
                results = filtered,
            });
        });

        grp.MapGet("/scanner", async (
            decimal? minVolume,
            decimal? minPct,
            decimal? maxPct,
            string? exclude,
            string? include,
            string? windowSize,
            string? direction,
            int? maxSymbols,
            IExchangeClient exchange,
            TickerSnapshotCache tickerCache,
            QuantFlowBotsDbContext db,
            CancellationToken ct) =>
        {
            var window = NormalizeWindowSize(windowSize);
            if (window is null)
                return Results.BadRequest(new { error = "invalid_window_size", allowed = "1m..59m, 1h..23h, 1d..7d" });

            var dir = (direction ?? "any").Trim().ToLowerInvariant();
            if (dir is not ("any" or "up" or "down"))
                return Results.BadRequest(new { error = "invalid_direction", allowed = "any | up | down" });

            var minVol = minVolume ?? 50_000_000m;
            var minP = minPct ?? 1m;
            var maxP = maxPct ?? 25m;
            var take = Math.Clamp(maxSymbols ?? 30, 1, 100);
            var blacklist = (exclude ?? "USDCUSDT,FDUSDUSDT,TUSDUSDT,BUSDUSDT,USDPUSDT,USDDUSDT,DAIUSDT,SUSDUSDT,USD1USDT,EURUSDT,EURIUSDT,AEURUSDT,EURTUSDT")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var whitelist = string.IsNullOrWhiteSpace(include)
                ? null
                : include.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var usdtSymbols = await db.Symbols
                .Where(s => s.IsActive && s.QuoteAsset == "USDT")
                .OrderBy(s => s.Code)
                .Select(s => s.Code)
                .ToListAsync(ct);

            if (whitelist is not null)
                usdtSymbols = usdtSymbols.Where(whitelist.Contains).ToList();

            // Pre-filter by 24h volume (single weight-80 call) to cap rolling-ticker fanout.
            // Calling /api/v3/ticker for 1000+ symbols burns 2000+ weight and 429s on Binance.
            var usdtSet = usdtSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pre = await tickerCache.GetAsync(ct);
            var preTop = pre
                .Where(t => usdtSet.Contains(t.Symbol))
                .Where(t => t.QuoteVolume >= minVol)
                .OrderByDescending(t => t.QuoteVolume)
                .Take(200)
                .Select(t => t.Symbol)
                .ToList();

            var tickers = window == "1d"
                ? pre.Where(t => preTop.Contains(t.Symbol)).ToList()
                : (IReadOnlyList<TickerSnapshot>)await exchange.GetRollingTickersAsync(preTop, window, ct);
            var usdtPairs = tickers.ToList();
            var passVolume = usdtPairs.Where(t => t.QuoteVolume >= minVol).ToList();
            var passPct = passVolume
                .Where(t => dir switch
                {
                    "up"   => t.PriceChangePercent >=  minP && t.PriceChangePercent <=  maxP,
                    "down" => t.PriceChangePercent <= -minP && t.PriceChangePercent >= -maxP,
                    _      => Math.Abs(t.PriceChangePercent) >= minP && Math.Abs(t.PriceChangePercent) <= maxP,
                })
                .ToList();
            var passBlacklist = passPct.Where(t => !blacklist.Contains(t.Symbol)).ToList();
            var passWhitelist = passBlacklist.ToList();

            var matches = passWhitelist
                .OrderByDescending(t => t.QuoteVolume)
                .Take(take)
                .Select(t => new
                {
                    symbol = t.Symbol,
                    price = t.LastPrice,
                    priceChangePercent = t.PriceChangePercent,
                    quoteVolume = t.QuoteVolume,
                })
                .ToList();

            // Compute near-misses to help user loosen the filter
            object? nearMissPct = null;
            if (passPct.Count == 0 && passVolume.Count > 0)
            {
                // Order near-miss by direction so the hint matches what the user is searching for.
                var ordered = dir switch
                {
                    "up"   => passVolume.OrderByDescending(t => t.PriceChangePercent),
                    "down" => passVolume.OrderBy(t => t.PriceChangePercent),
                    _      => passVolume.OrderByDescending(t => Math.Abs(t.PriceChangePercent)),
                };
                var closest = ordered
                    .Take(5)
                    .Select(t => new { symbol = t.Symbol, priceChangePercent = t.PriceChangePercent, quoteVolume = t.QuoteVolume })
                    .ToList();
                nearMissPct = new { maxAbsPctSeen = closest.Count > 0 ? Math.Abs(closest[0].priceChangePercent) : 0m, samples = closest };
            }

            return Results.Ok(new
            {
                updatedAt = DateTimeOffset.UtcNow,
                filter = new { minVolume = minVol, minPct = minP, maxPct = maxP, windowSize = window, direction = dir, excludeCount = blacklist.Count, includeCount = whitelist?.Count ?? 0, maxSymbols = take },
                stages = new
                {
                    totalUsdtPairs = usdtSymbols.Count,
                    afterVolume = passVolume.Count,
                    afterPctRange = passPct.Count,
                    afterBlacklist = passBlacklist.Count,
                    afterWhitelist = passWhitelist.Count,
                },
                count = matches.Count,
                results = matches,
                nearMissPct,
            });
        }).RequireRateLimiting("scanner");

        grp.MapGet("/symbols", async (QuantFlowBotsDbContext db, CancellationToken ct) =>
        {
            var list = await db.Symbols
                .Where(s => s.IsActive)
                .OrderBy(s => s.Code)
                .Select(s => new { s.Id, s.Code, s.BaseAsset, s.QuoteAsset })
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        grp.MapGet("/candles", async (
            string symbol,
            string interval,
            int? limit,
            IExchangeClient exchange,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<CandleInterval>(interval, true, out var iv))
                return Results.BadRequest(new { error = $"Unknown interval: {interval}" });
            var candles = await exchange.GetCandlesAsync(symbol, iv, null, null, limit ?? 200, ct);
            return Results.Ok(candles);
        });

        return app;
    }

    private static string? NormalizeWindowSize(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "1h" : raw.Trim().ToLowerInvariant();
        if (value == "24h") value = "1d";
        if (value.EndsWith('m') && int.TryParse(value[..^1], out var minutes) && minutes is >= 1 and <= 59)
            return $"{minutes}m";
        if (value.EndsWith('h') && int.TryParse(value[..^1], out var hours) && hours is >= 1 and <= 23)
            return $"{hours}h";
        if (value.EndsWith('d') && int.TryParse(value[..^1], out var days) && days is >= 1 and <= 7)
            return $"{days}d";
        return null;
    }

    private static bool IsExcludedStablePair(string symbol, IReadOnlyList<string> quoteAssets, ISet<string> excludedBaseAssets)
    {
        foreach (var quote in quoteAssets.OrderByDescending(q => q.Length))
        {
            if (!symbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase)) continue;
            var baseAsset = symbol[..^quote.Length];
            return excludedBaseAssets.Contains(baseAsset);
        }
        return false;
    }
}
