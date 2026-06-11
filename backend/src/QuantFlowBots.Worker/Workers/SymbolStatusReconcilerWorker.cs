using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Risk;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Worker.Workers;

/// <summary>
/// Cross-checks our active USDT symbols against Binance /api/v3/exchangeInfo every 30 minutes.
/// When Binance reports status != "TRADING" (HALT / BREAK / AUCTION_MATCH) or the symbol has
/// vanished entirely, we:
///   - Set Symbol.IsActive = false in the DB (durable — survives restart)
///   - Block in SymbolRiskGate (live — stops bot dispatch immediately)
/// </summary>
public sealed class SymbolStatusReconcilerWorker(
    IServiceScopeFactory scopeFactory,
    BinanceRestClient binance,
    SymbolRiskGate riskGate,
    ILogger<SymbolStatusReconcilerWorker> logger) : BackgroundService
{
    // 5min cadence: /api/v3/exchangeInfo is weight=20 — even 1/min would be fine. We pick 5min
    // to ensure status flips reach the bot within 5min of Binance flagging the symbol.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SymbolStatusReconcilerWorker started.");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileAsync(stoppingToken); }
            catch (Exception ex) { logger.LogWarning(ex, "SymbolStatusReconciler failed"); }
            try { await Task.Delay(PollInterval, stoppingToken); } catch { return; }
        }
    }

    // Flag cũ hơn ngưỡng + symbol không còn position mở → safe để clear khỏi UI.
    // Lý do: delist đã settle, auto-close position đã chạy từ trước (T+0), giữ flag chỉ làm
    // pollute UI. Symbol vẫn IsActive=false trong DB — nếu user tự retry thì gate sẽ block lại
    // khi BinanceAnnouncementWorker thấy lại tin xấu, hoặc khi user link bot tới symbol delist.
    private static readonly TimeSpan StaleFlagAge = TimeSpan.FromDays(7);

    private async Task ReconcileAsync(CancellationToken ct)
    {
        // BinanceRestClient.GetSymbolsAsync filters status==TRADING, so use a raw call here to
        // observe non-trading rows too.
        // Reuse the typed client's underlying HttpClient via reflection-free path: just call the
        // public method again — it already returns only TRADING symbols. We compare presence/absence.
        var live = await binance.GetSymbolsAsync(ct);
        var liveCodes = live.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

        var ours = await db.Symbols.Where(s => s.IsActive && s.QuoteAsset == "USDT").ToListAsync(ct);
        var newlyDelisted = 0;
        foreach (var s in ours)
        {
            if (liveCodes.Contains(s.Code)) continue;
            // Symbol either delisted or moved to non-TRADING status — both mean don't trade it.
            s.IsActive = false;
            await riskGate.BlockAsync(s.Code, "binance_status_not_trading", "exchange_info", null, ct);
            newlyDelisted++;
            logger.LogWarning("Symbol {Symbol} no longer TRADING on Binance → deactivated + risk-blocked", s.Code);
        }
        if (newlyDelisted > 0) await db.SaveChangesAsync(ct);

        await PurgeStaleRiskFlagsAsync(db, ct);
    }

    /// <summary>
    /// Xóa risk flag cũ > <see cref="StaleFlagAge"/> nếu symbol KHÔNG còn open position.
    /// Logic: delist sau 7 ngày = đã settle hoàn toàn (Binance ngừng giao dịch, auto-close
    /// position đã chạy ở T+0). Giữ entry chỉ làm UI list dài vô tận. Không phá durability —
    /// nếu sau này có tin xấu mới, BinanceAnnouncementWorker sẽ re-block ngay.
    /// </summary>
    private async Task PurgeStaleRiskFlagsAsync(QuantFlowBotsDbContext db, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - StaleFlagAge;
        var oldFlags = await db.SymbolRiskFlags
            .Where(f => f.At < cutoff)
            .Select(f => new { f.Id, f.Symbol })
            .ToListAsync(ct);
        if (oldFlags.Count == 0) return;

        // Position mở per symbol — tránh purge flag mà bot vẫn còn lệnh chưa close (edge case
        // nếu auto-close fail). Lookup theo symbol code qua FK.
        var symbolsWithOpen = await db.Positions
            .Where(p => p.Status == Domain.Enums.PositionStatus.Open)
            .Select(p => p.Symbol!.Code)
            .Distinct()
            .ToListAsync(ct);
        var openSet = symbolsWithOpen.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var purged = 0;
        foreach (var f in oldFlags)
        {
            if (openSet.Contains(f.Symbol)) continue;
            // Xóa qua riskGate.UnblockAsync — vừa xóa DB row vừa update in-memory dict.
            if (await riskGate.UnblockAsync(f.Symbol, ct)) purged++;
        }
        if (purged > 0)
            logger.LogInformation("PurgeStaleRiskFlags: cleared {Count} flags older than {Days}d (no open positions)",
                purged, StaleFlagAge.TotalDays);
    }
}
