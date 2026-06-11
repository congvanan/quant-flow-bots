using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

public enum SymbolTier { Unknown = 0, Top = 1, Mid = 2, Low = 3 }

/// <summary>
/// Phân loại symbol thành Top / Mid / Low theo 24h quote volume (USDT).
/// - Top:  rank 1-20    (BTCUSDT, ETHUSDT, BNBUSDT, SOLUSDT, ...)
/// - Mid:  rank 21-100
/// - Low:  rank 101+
/// Refresh từ <see cref="TickerSnapshotCache"/> (đã có TTL 10s + serve-stale 2 phút) —
/// chỉ làm 1 pass sort + dictionary update khi cache miss. Worker đọc <see cref="GetTier"/>
/// trên hot path, phải O(1), nên kết quả lưu trong <see cref="ConcurrentDictionary{TKey,TValue}"/>.
///
/// Vì sao không lưu rank/volume cụ thể: nguồn duy nhất cần là "tier nào để chọn ngưỡng",
/// và rank dao động liên tục → cache rank gây churn không cần thiết. Tier coarse-grain
/// (3 mức) ổn định hơn nhiều.
/// </summary>
public sealed class SymbolTierResolver
{
    private const int TopRankMax = 20;
    private const int MidRankMax = 100;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);

    private readonly TickerSnapshotCache _ticker;
    private readonly ILogger<SymbolTierResolver> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private ConcurrentDictionary<string, SymbolTier> _tiers = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public SymbolTierResolver(TickerSnapshotCache ticker, ILogger<SymbolTierResolver> logger)
    {
        _ticker = ticker;
        _logger = logger;
    }

    /// <summary>Hot-path lookup. Trả Unknown nếu chưa có dữ liệu — caller tự quyết fallback.</summary>
    public SymbolTier GetTier(string symbol)
        => _tiers.TryGetValue(symbol, out var t) ? t : SymbolTier.Unknown;

    /// <summary>
    /// Trigger refresh nếu quá <see cref="RefreshInterval"/> kể từ lần thành công gần nhất.
    /// Idempotent + thread-safe. Worker gọi định kỳ (không gọi trên mỗi event).
    /// </summary>
    public async Task EnsureFreshAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastRefresh < RefreshInterval && _tiers.Count > 0) return;

        if (!await _refreshGate.WaitAsync(0, ct)) return; // peer đang refresh — bỏ qua, tránh xếp hàng
        try
        {
            if (DateTimeOffset.UtcNow - _lastRefresh < RefreshInterval && _tiers.Count > 0) return;

            var snapshot = await _ticker.GetAsync(ct);
            if (snapshot.Count == 0) return;

            // Chỉ rank USDT pair — quote khác (BUSD, FDUSD, BTC pair) không phải target.
            var ranked = snapshot
                .Where(s => s.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.QuoteVolume)
                .ToList();

            var next = new ConcurrentDictionary<string, SymbolTier>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < ranked.Count; i++)
            {
                var tier = (i + 1) <= TopRankMax ? SymbolTier.Top
                         : (i + 1) <= MidRankMax ? SymbolTier.Mid
                         : SymbolTier.Low;
                next[ranked[i].Symbol] = tier;
            }

            _tiers = next;
            _lastRefresh = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "SymbolTierResolver refreshed: {Total} USDT pairs (Top≤{Top}, Mid≤{Mid}, Low rest)",
                ranked.Count, TopRankMax, MidRankMax);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SymbolTierResolver refresh failed — giữ snapshot tier cũ");
        }
        finally { _refreshGate.Release(); }
    }
}
