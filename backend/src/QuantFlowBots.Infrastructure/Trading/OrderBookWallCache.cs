using System.Collections.Concurrent;
using QuantFlowBots.Application.Streaming;

namespace QuantFlowBots.Infrastructure.Trading;

/// <summary>
/// Latest order-book walls keyed by (Symbol, Side, Price). TTL drops stale entries
/// so the read endpoint never returns walls that were pulled hours ago and may have
/// been canceled on the exchange. Worker re-publishes alive walls on every scan,
/// which refreshes the entry's `At`.
/// </summary>
public sealed class OrderBookWallCache
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, OrderBookWallEvent> _byKey = new();

    public OrderBookWallCache() : this(TimeSpan.FromMinutes(5)) { }
    public OrderBookWallCache(TimeSpan ttl) { _ttl = ttl; }

    public void Upsert(OrderBookWallEvent evt)
    {
        var key = $"{evt.Symbol}|{evt.Side}|{evt.Price}";
        _byKey[key] = evt;
    }

    public IReadOnlyList<OrderBookWallEvent> Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<OrderBookWallEvent>(_byKey.Count);
        foreach (var (key, evt) in _byKey)
        {
            if (now - evt.At > _ttl) { _byKey.TryRemove(key, out _); continue; }
            result.Add(evt);
        }
        return result;
    }
}
