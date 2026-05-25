using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Infrastructure.Exchanges.Binance;

namespace QuantFlowBots.Infrastructure.Trading.LiveTrading;

/// <summary>
/// Per-base-url cache of futures exchange info. Loaded lazily on first request,
/// refreshed when older than RefreshAfter. Spot OrderValidator does NOT cover futures
/// because LOT_SIZE / stepSize / tickSize / minNotional differ. Live executor MUST
/// validate against this before any signed order.
/// </summary>
public sealed class FuturesSymbolFiltersCache(
    BinanceFuturesRestClient futures,
    ILogger<FuturesSymbolFiltersCache> logger)
{
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(6);
    private readonly ConcurrentDictionary<string, CacheEntry> _byBaseUrl = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<FuturesSymbolFilter?> GetAsync(string baseUrl, string symbolCode, CancellationToken cancellationToken)
    {
        var entry = await EnsureLoadedAsync(baseUrl, cancellationToken);
        return entry?.BySymbol.GetValueOrDefault(symbolCode.ToUpperInvariant());
    }

    public async Task<FuturesQtyValidation> ValidateAsync(
        string baseUrl, string symbolCode, decimal quantity, decimal price, CancellationToken cancellationToken)
    {
        var filter = await GetAsync(baseUrl, symbolCode, cancellationToken);
        if (filter is null) return new(false, 0m, 0m, "symbol_not_listed_on_futures");

        var qty = RoundDown(quantity, filter.StepSize);
        if (qty < filter.MinQuantity)
            return new(false, qty, price, $"qty_below_min ({qty} < {filter.MinQuantity})");

        var px = filter.TickSize > 0 ? RoundDown(price, filter.TickSize) : price;
        if (filter.MinNotional > 0 && qty * px < filter.MinNotional)
            return new(false, qty, px, $"notional_below_min ({qty * px} < {filter.MinNotional})");

        return new(true, qty, px, null);
    }

    private async Task<CacheEntry?> EnsureLoadedAsync(string baseUrl, CancellationToken cancellationToken)
    {
        if (_byBaseUrl.TryGetValue(baseUrl, out var existing) && DateTimeOffset.UtcNow - existing.LoadedAt < RefreshAfter)
            return existing;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_byBaseUrl.TryGetValue(baseUrl, out existing) && DateTimeOffset.UtcNow - existing.LoadedAt < RefreshAfter)
                return existing;

            var list = await futures.GetExchangeInfoAsync(baseUrl, cancellationToken);
            var dict = list.ToDictionary(f => f.Symbol, f => f, StringComparer.OrdinalIgnoreCase);
            var fresh = new CacheEntry(dict, DateTimeOffset.UtcNow);
            _byBaseUrl[baseUrl] = fresh;
            logger.LogInformation("Loaded {Count} futures symbols from {BaseUrl}", list.Length, baseUrl);
            return fresh;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Futures exchangeInfo refresh failed for {BaseUrl}", baseUrl);
            return _byBaseUrl.GetValueOrDefault(baseUrl);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static decimal RoundDown(decimal value, decimal step)
    {
        if (step <= 0m) return value;
        return Math.Floor(value / step) * step;
    }

    private sealed record CacheEntry(IReadOnlyDictionary<string, FuturesSymbolFilter> BySymbol, DateTimeOffset LoadedAt);
}

public sealed record FuturesQtyValidation(bool Ok, decimal AdjustedQuantity, decimal AdjustedPrice, string? Reason);
