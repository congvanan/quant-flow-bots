using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Cache klines theo (symbol, interval). Lý do tồn tại:
///
/// WhaleAlertWorker quét ~300 symbol × 3 interval = ~900 REST call mỗi tick (60s). Mỗi
/// call fetch ~200 candles. Không cache → worker liên tục deserialize ~80k candle/phút,
/// CPU 50%+, GC chạy nóng, tick không bao giờ kịp hoàn thành trong 60s.
///
/// Nến đã đóng (closed) không bao giờ đổi. Chỉ nến cuối (intrabar) thay đổi. Cache
/// theo TTL = max(30s, intervalSeconds / 4):
///   - 5m bar  → TTL 75s   (~tương đương 1 tick)
///   - 15m bar → TTL 225s  (~4 ticks ăn cache)
///   - 1h bar  → TTL 900s  (~15 ticks ăn cache)
///
/// Trade-off: với intrabar mode trên timeframe lớn, alert có thể trễ tối đa = TTL.
/// Chấp nhận được vì người dùng đã chọn timeframe lớn = ít nhạy theo nature.
///
/// Singleton service. Caller truyền vào <see cref="BinanceRestClient"/> từ scope của họ.
/// </summary>
public sealed class KlineCache(ILogger<KlineCache> logger)
{
    private sealed record Entry(IReadOnlyList<CandleData> Candles, DateTimeOffset FetchedAt, long Tick);

    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private long _hits, _misses, _accessTick;
    // Hard upper bound số entry. 150 symbol × 4 interval = 600 entry max. Trên ngưỡng
    // sẽ evict LRU theo Tick (entry chưa được đọc lâu nhất bị xóa). Tránh cache phình
    // vô hạn khi user đổi minVolume24h liên tục hoặc symbol mới được list.
    private const int MaxEntries = 600;

    public async Task<IReadOnlyList<CandleData>> GetAsync(
        BinanceRestClient binance,
        string symbol,
        CandleInterval interval,
        int limit,
        CancellationToken ct)
    {
        var key = $"{symbol}|{(int)interval}";
        var ttlSec = Math.Max(30, (int)interval / 4);
        var now = DateTimeOffset.UtcNow;

        if (_cache.TryGetValue(key, out var e)
            && e.Candles.Count >= limit
            && (now - e.FetchedAt).TotalSeconds < ttlSec)
        {
            Interlocked.Increment(ref _hits);
            // Bump access tick để LRU đánh dấu entry này "vừa được dùng".
            _cache[key] = e with { Tick = Interlocked.Increment(ref _accessTick) };
            return e.Candles;
        }

        Interlocked.Increment(ref _misses);
        var fresh = await binance.GetCandlesAsync(symbol, interval, null, null, limit, ct);
        _cache[key] = new Entry(fresh, now, Interlocked.Increment(ref _accessTick));
        if (_cache.Count > MaxEntries) EvictLru();
        return fresh;
    }

    /// <summary>Evict ~10% entry có Tick thấp nhất khi cache vượt MaxEntries.</summary>
    private void EvictLru()
    {
        var target = MaxEntries * 9 / 10; // giảm xuống 90% để tránh evict liên tục
        var excess = _cache.Count - target;
        if (excess <= 0) return;
        var victims = _cache.ToArray()
            .OrderBy(kv => kv.Value.Tick)
            .Take(excess)
            .Select(kv => kv.Key)
            .ToArray();
        foreach (var k in victims) _cache.TryRemove(k, out _);
    }

    /// <summary>Drop entries cũ hơn cutoff để cache không phình mãi (symbol bị delist, v.v.).</summary>
    public void Prune(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var removed = 0;
        foreach (var (k, e) in _cache.ToArray())
        {
            if (e.FetchedAt < cutoff && _cache.TryRemove(k, out _)) removed++;
        }
        if (removed > 0 || _hits + _misses > 0)
        {
            var total = _hits + _misses;
            var hitRate = total > 0 ? _hits * 100.0 / total : 0;
            logger.LogInformation("KlineCache: {Hits}/{Total} hits ({Rate:F0}%), {Size} entries, pruned {Pruned}",
                _hits, total, hitRate, _cache.Count, removed);
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _misses, 0);
        }
    }
}
