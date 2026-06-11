using System.Globalization;
using StackExchange.Redis;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Redis-backed cache cho LAST PRICE của TẤT CẢ futures symbols (không phải chỉ alpha).
/// Phục vụ <see cref="QuantFlowBots.Application.Trading.IBasisGuard"/> + bất kỳ consumer
/// nào cần futures price nhanh.
///
/// **Vì sao tách khỏi <see cref="AlphaPriceCache"/>**: AlphaPriceCache giới hạn 216 alpha
/// symbols để Alpha UI snapshot nhẹ. BasisGuard cần phủ MỌI futures symbol (BTCUSDT,
/// ETHUSDT, …) — không thuộc nhóm alpha vẫn cần guard. 2 hash độc lập, populate cùng 1
/// poll trong AlphaPriceStreamWorker.
///
/// Layout:
///   HASH key  = "futures:prices"
///   field     = symbol (e.g. BTCUSDT)
///   value     = "{price}|{epochMs}"  (text format, nhẹ hơn JSON, đủ cho 1 reader)
///   TTL       = 2 phút (worker poll 3s liên tục → expiry sẽ luôn refresh).
/// </summary>
public sealed class FuturesPriceCache(IConnectionMultiplexer redis)
{
    private const string HashKey = "futures:prices";
    private static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(2);

    public void Upsert(string symbol, decimal price, DateTimeOffset at)
    {
        var db = redis.GetDatabase();
        // Text format thay vì JSON: 1 record = 1 reader (BasisGuard), không cần schema.
        var value = $"{price.ToString(CultureInfo.InvariantCulture)}|{at.ToUnixTimeMilliseconds()}";
        db.HashSet(HashKey, symbol, value, flags: CommandFlags.FireAndForget);
        if (Random.Shared.Next(500) == 0)
            db.KeyExpire(HashKey, KeyTtl, flags: CommandFlags.FireAndForget);
    }

    public (decimal Price, DateTimeOffset At)? TryGet(string symbol)
    {
        var db = redis.GetDatabase();
        var raw = (string?)db.HashGet(HashKey, symbol);
        if (string.IsNullOrEmpty(raw)) return null;
        var parts = raw.Split('|', 2);
        if (parts.Length != 2) return null;
        if (!decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var price)) return null;
        if (!long.TryParse(parts[1], out var ms)) return null;
        return (price, DateTimeOffset.FromUnixTimeMilliseconds(ms));
    }
}
