using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>Snapshot giá realtime cho 1 alpha futures symbol — chỉ field nhẹ, không sparkline/marketCap.</summary>
public sealed record AlphaPriceTick(
    string Symbol,
    decimal Price,
    decimal PercentChange24h,
    decimal High24h,
    decimal Low24h,
    decimal QuoteVolume24h,
    // Funding (USDT-M perpetual): rate là tỷ lệ FUNDING_PERIOD-ly (Binance mặc định 8h),
    // dương = long trả short, âm = short trả long. NextFundingTime là epoch ms UTC.
    decimal FundingRate,
    DateTimeOffset? NextFundingTime,
    DateTimeOffset At);

/// <summary>
/// Redis-backed cache cho realtime price ticks của alpha symbols.
///
/// **Vì sao Redis (không phải ConcurrentDictionary in-memory)**:
/// API và Worker là 2 PROCESS độc lập (xem CLAUDE memory: "mọi state chia sẻ phải qua
/// Redis"). Singleton ConcurrentDictionary chỉ sống trong 1 process — nếu Worker ghi, API
/// vẫn đọc cache rỗng của chính nó. Redis là kênh hợp nhất duy nhất.
///
/// Layout:
///   HASH key  = "alpha:prices"
///   field     = futuresSymbol (e.g. "LABUSDT")
///   value     = JSON {price, pct, high, low, qv, at}
///   TTL       = 5 phút trên cả HASH (Worker tiếp tục ghi → expiry refresh).
///
/// Cost: HSET ~216 ops/s (1/symbol/s) = không đáng kể với Redis. HGETALL trả ~10KB
/// payload mỗi 2s từ API endpoint = ổn.
/// </summary>
public sealed class AlphaPriceCache(IConnectionMultiplexer redis)
{
    private const string HashKey = "alpha:prices";
    private static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(5);

    public void Upsert(AlphaPriceTick tick)
    {
        // Fire-and-forget cho throughput: hot path nhận 1 tick/symbol/s × ~220 symbols.
        // CommandFlags.FireAndForget bỏ qua reply → ~5x throughput trên StackExchange.Redis.
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(tick);
        db.HashSet(HashKey, tick.Symbol, json, flags: CommandFlags.FireAndForget);
        // KeyExpire mỗi tick là phí — chỉ refresh xác suất ~1/200 ticks ≈ mỗi 1s/symbol/200
        // ≈ 1 lần / 200s tổng cho toàn cache. TTL 5 phút an toàn.
        if (Random.Shared.Next(200) == 0)
            db.KeyExpire(HashKey, KeyTtl, flags: CommandFlags.FireAndForget);
    }

    public IReadOnlyList<AlphaPriceTick> Snapshot()
    {
        var db = redis.GetDatabase();
        var entries = db.HashGetAll(HashKey);
        if (entries.Length == 0) return [];
        var result = new List<AlphaPriceTick>(entries.Length);
        foreach (var e in entries)
        {
            try
            {
                var tick = JsonSerializer.Deserialize<AlphaPriceTick>(e.Value.ToString());
                if (tick is not null) result.Add(tick);
            }
            catch { /* skip malformed entry — Worker sẽ overwrite tick tới */ }
        }
        return result;
    }

    public int Count
    {
        get
        {
            var db = redis.GetDatabase();
            return (int)db.HashLength(HashKey);
        }
    }

    // Helpers giữ lại cho test/health check; không gọi từ hot path.
    private static decimal ParseDec(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
