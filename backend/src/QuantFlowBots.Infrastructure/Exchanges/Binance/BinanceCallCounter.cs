using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Per-process counter of outbound Binance REST calls, grouped by URL path.
/// A companion <see cref="BinanceCallCounterFlushService"/> dumps and resets every 60s.
/// Why: gives us a permanent baseline of who is calling what, so we can spot regressions
/// (e.g. a new feature that quietly burns weight) without having to re-run an audit.
/// </summary>
public sealed class BinanceCallCounter
{
    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.OrdinalIgnoreCase);

    public void Increment(string endpoint) =>
        _counts.AddOrUpdate(endpoint, 1, (_, v) => v + 1);

    public IReadOnlyDictionary<string, long> SnapshotAndReset()
    {
        var snap = _counts.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var key in snap.Keys) _counts.TryRemove(key, out _);
        return snap;
    }
}

public sealed class BinanceCallCounterFlushService(
    BinanceCallCounter counter,
    ILogger<BinanceCallCounterFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); } catch { return; }
            var snap = counter.SnapshotAndReset();
            if (snap.Count == 0) continue;
            var total = snap.Values.Sum();
            var top = string.Join(", ", snap.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key}={kv.Value}"));
            logger.LogInformation("Binance REST calls last 60s: total={Total} top=[{Top}]", total, top);
        }
    }
}
