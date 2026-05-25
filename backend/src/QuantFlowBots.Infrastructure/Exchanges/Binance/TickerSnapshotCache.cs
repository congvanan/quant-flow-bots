using Microsoft.Extensions.DependencyInjection;
using QuantFlowBots.Application.Exchanges;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// In-process cache for /api/v3/ticker/24hr.
/// Why: FE has 5+ widgets that all hit market endpoints on poll/refresh. Without a cache each
/// becomes a fresh Binance call — 5×N requests/min easily trips the 1200/min IP weight limit
/// and Binance returns 418 (I'm a teapot, IP banned). One in-process refresh per ~5s + shared
/// read fixes that AND survives transient Binance failures by returning the last good snapshot.
/// </summary>
public sealed class TickerSnapshotCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _staleGrace = TimeSpan.FromMinutes(2);
    private IReadOnlyList<TickerSnapshot> _snapshot = [];
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;

    public TickerSnapshotCache(IServiceScopeFactory scopeFactory) { _scopeFactory = scopeFactory; }

    public async Task<IReadOnlyList<TickerSnapshot>> GetAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSuccess < _ttl && _snapshot.Count > 0) return _snapshot;

        await _gate.WaitAsync(ct);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (now - _lastSuccess < _ttl && _snapshot.Count > 0) return _snapshot;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<BinanceRestClient>();
                var fresh = await client.GetAllTickersAsync(ct);
                if (fresh.Count > 0)
                {
                    _snapshot = fresh;
                    _lastSuccess = DateTimeOffset.UtcNow;
                }
                return _snapshot;
            }
            catch (HttpRequestException)
            {
                // Binance 418/429/5xx — serve last good snapshot if within stale grace window.
                if (_snapshot.Count > 0 && DateTimeOffset.UtcNow - _lastSuccess < _staleGrace)
                    return _snapshot;
                throw;
            }
        }
        finally { _gate.Release(); }
    }
}
