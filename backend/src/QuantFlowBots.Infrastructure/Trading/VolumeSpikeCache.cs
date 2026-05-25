using System.Collections.Concurrent;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Streaming;

namespace QuantFlowBots.Infrastructure.Trading;

public sealed class VolumeSpikeCache
{
    private const int RollingWindow = 20;
    private const int MaxRecentSpikes = 50;

    private readonly ConcurrentDictionary<string, RollingBuffer> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<VolumeSpikeEvent> _recent = new();
    private readonly object _trimLock = new();

    public RollingBuffer GetOrCreateBuffer(string symbol) =>
        _buffers.GetOrAdd(symbol, _ => new RollingBuffer(RollingWindow + 2));

    public void Add(VolumeSpikeEvent spike)
    {
        _recent.Enqueue(spike);
        lock (_trimLock)
        {
            while (_recent.Count > MaxRecentSpikes && _recent.TryDequeue(out _)) { }
        }
    }

    public IReadOnlyList<VolumeSpikeEvent> Snapshot()
    {
        return _recent.ToArray().OrderByDescending(s => s.At).ToList();
    }
}

public sealed class RollingBuffer
{
    private readonly LinkedList<CandleData> _items = new();
    private readonly int _capacity;
    private readonly object _lock = new();

    public RollingBuffer(int capacity) => _capacity = capacity;

    public void Add(CandleData candle)
    {
        lock (_lock)
        {
            if (_items.Count > 0 && _items.Last!.Value.OpenTime == candle.OpenTime)
            {
                _items.RemoveLast();
            }
            _items.AddLast(candle);
            while (_items.Count > _capacity) _items.RemoveFirst();
        }
    }

    public IReadOnlyList<CandleData> Snapshot()
    {
        lock (_lock)
        {
            return _items.ToArray();
        }
    }
}
