using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Background warmer cho <see cref="AlphaMarketService"/> trong API process.
///
/// **Vì sao cần**: AlphaMarketService là Singleton scoped TRONG PROCESS. Worker đã warm cache
/// của chính nó (qua AlphaPriceStreamWorker), nhưng instance của API process là riêng — vẫn
/// cold đến khi có request đầu tiên. Request đầu phải chờ pipeline build (~2-3s: alpha list +
/// futures exchangeInfo + 216 sparkline calls throttle 10 concurrent).
///
/// Warmer chạy ngay khi API start + refresh mỗi 9 phút (trước TTL 10 phút) để cache luôn warm
/// khi user vào trang. Không gây weight burst lên Binance (chỉ 1 lần/9 phút).
/// </summary>
public sealed class AlphaCacheWarmer(
    AlphaMarketService alpha,
    ILogger<AlphaCacheWarmer> logger) : BackgroundService
{
    // 9 phút < TTL 10 phút trong AlphaMarketService → refresh chủ động trước khi cache expire.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(9);
    // Initial delay nhỏ cho API container settle (Postgres migration, JWT keys…) trước khi
    // bắt đầu fetch Binance — tránh race với startup retry.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var snapshot = await alpha.GetAsync(stoppingToken);
                sw.Stop();
                logger.LogInformation("AlphaCacheWarmer refreshed: {Count} tokens in {Ms}ms",
                    snapshot.Count, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Stale grace của AlphaMarketService đã handle ở layer dưới — log warning đủ.
                logger.LogWarning(ex, "AlphaCacheWarmer refresh failed — will retry next cycle");
            }

            try { await Task.Delay(RefreshInterval, stoppingToken); } catch { return; }
        }
    }
}
