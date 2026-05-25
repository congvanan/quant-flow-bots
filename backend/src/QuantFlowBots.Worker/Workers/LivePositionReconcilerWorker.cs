using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Security;

namespace QuantFlowBots.Worker.Workers;

/// <summary>
/// Truth source for Live positions = exchange. Every 30s, for each running bot with
/// RunMode=LiveTrading, fetch /fapi/v2/positionRisk for its symbol and reconcile DB:
///   exchange position 0 + DB has Open Live  → mark closed (server-side SL/TP fired)
///   exchange > 0  + DB has no Open Live     → emit RiskEvent("orphan_live_position")
/// Never auto-creates Position rows.
/// </summary>
public sealed class LivePositionReconcilerWorker(
    IServiceScopeFactory scopeFactory,
    IBotEventBus botBus,
    IBinanceGate gate,
    ILogger<LivePositionReconcilerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly Random _rng = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("LivePositionReconcilerWorker started ({Interval}s).", Interval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var state = await gate.GetStateAsync(stoppingToken);
            if (state.IsOpen)
            {
                var wait = state.Until!.Value - DateTimeOffset.UtcNow + Jitter(TimeSpan.FromSeconds(3));
                if (wait < TimeSpan.FromSeconds(5)) wait = TimeSpan.FromSeconds(5);
                logger.LogWarning("Reconciler: gate OPEN, sleeping {Wait}s until {Until}", (int)wait.TotalSeconds, state.Until);
                try { await Task.Delay(wait, stoppingToken); } catch { return; }
                continue;
            }

            try { await TickAsync(stoppingToken); }
            catch (BinanceGateOpenException) { /* gate tripped mid-tick; next loop will yield */ }
            catch (Exception ex) { logger.LogError(ex, "Reconciler tick failed"); }

            var jittered = Interval + Jitter(Interval * 0.2);
            try { await Task.Delay(jittered, stoppingToken); } catch { return; }
        }
    }

    private static TimeSpan Jitter(TimeSpan span) =>
        TimeSpan.FromMilliseconds((_rng.NextDouble() * 2 - 1) * span.TotalMilliseconds);

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<IApiKeyEncryption>();
        var futures = scope.ServiceProvider.GetRequiredService<BinanceFuturesRestClient>();
        var spot = scope.ServiceProvider.GetRequiredService<BinanceSpotSignedClient>();

        var liveBots = await db.Bots
            .Include(b => b.Symbol)
            .Where(b => b.State == BotState.Running && b.RunMode == BotRunMode.LiveTrading && b.ApiKeyId != null)
            .ToListAsync(cancellationToken);
        if (liveBots.Count == 0) return;

        foreach (var bot in liveBots)
        {
            if (bot.Symbol is null || bot.ApiKeyId is null) continue;
            var key = await db.ApiKeys.Include(k => k.Exchange)
                .FirstOrDefaultAsync(k => k.Id == bot.ApiKeyId, cancellationToken);
            if (key?.Exchange is null) continue;

            if (key.Exchange.Code == "binance-spot-testnet")
            {
                await ReconcileSpotAsync(db, spot, bot, key, encryption, cancellationToken);
                continue;
            }

            var cred = new FuturesCredential(
                encryption.Decrypt(key.EncryptedKey),
                encryption.Decrypt(key.EncryptedSecret),
                key.Exchange.RestBaseUrl);

            FuturesPosition? snap;
            try { snap = await futures.GetPositionRiskAsync(cred, bot.Symbol.Code, cancellationToken); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "positionRisk failed bot={BotId} {Symbol}", bot.Id, bot.Symbol.Code);
                continue;
            }
            if (snap is null) continue;

            var dbOpen = await db.Positions
                .Where(p => p.BotId == bot.Id && p.SymbolId == bot.SymbolId
                            && p.Status == PositionStatus.Open && p.Mode == TradingMode.Live)
                .FirstOrDefaultAsync(cancellationToken);

            var exchangeQty = Math.Abs(snap.PositionAmt);

            if (exchangeQty == 0m && dbOpen is not null)
            {
                dbOpen.Status = PositionStatus.Closed;
                dbOpen.ExitPrice = snap.MarkPrice;
                dbOpen.ClosedAt = DateTimeOffset.UtcNow;
                dbOpen.CloseReason ??= "reconciled_exchange_closed";
                dbOpen.RealizedPnl = (snap.MarkPrice - dbOpen.EntryPrice) * dbOpen.Quantity;
                await db.SaveChangesAsync(cancellationToken);
                await botBus.PublishAsync(new BotEvent(bot.Id, "auto_close",
                    $"LIVE position reconciled closed @ {snap.MarkPrice:F4} (server SL/TP fired)",
                    DateTimeOffset.UtcNow), cancellationToken);
                logger.LogInformation("Reconciler closed DB position bot={BotId} {Symbol}", bot.Id, bot.Symbol.Code);
            }
            else if (exchangeQty > 0m && dbOpen is null)
            {
                // Orphan — exchange has position we didn't open. Log a risk event; don't create rows blindly.
                var existsOrphan = await db.RiskEvents.AnyAsync(r =>
                    r.BotId == bot.Id && r.EventType == "orphan_live_position"
                    && r.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1), cancellationToken);
                if (!existsOrphan)
                {
                    db.RiskEvents.Add(new RiskEvent
                    {
                        BotId = bot.Id,
                        UserId = bot.UserId,
                        EventType = "orphan_live_position",
                        Severity = "warn",
                        Message = $"Exchange shows position {snap.PositionAmt} {bot.Symbol.Code} but DB has none.",
                        ActionTaken = "logged_only",
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    await botBus.PublishAsync(new BotEvent(bot.Id, "risk",
                        $"orphan live position {snap.PositionAmt} {bot.Symbol.Code}",
                        DateTimeOffset.UtcNow), cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Spot reconcile — no positionRisk endpoint. Compare DB open vs exchange free balance
    /// for the bot's base asset (BTC for BTCUSDT, ETH for ETHUSDT…). Balance &lt; expected qty
    /// minus a small tolerance means the position was sold elsewhere or never settled — close DB.
    /// </summary>
    private async Task ReconcileSpotAsync(
        QuantFlowBotsDbContext db,
        BinanceSpotSignedClient spot,
        Bot bot,
        ApiKey key,
        IApiKeyEncryption encryption,
        CancellationToken cancellationToken)
    {
        if (bot.Symbol is null || key.Exchange is null) return;
        var dbOpen = await db.Positions
            .Where(p => p.BotId == bot.Id && p.SymbolId == bot.SymbolId
                        && p.Status == PositionStatus.Open && p.Mode == TradingMode.Live)
            .FirstOrDefaultAsync(cancellationToken);
        if (dbOpen is null) return; // spot reconciler only closes; doesn't create orphan rows

        var cred = new SpotCredential(
            encryption.Decrypt(key.EncryptedKey),
            encryption.Decrypt(key.EncryptedSecret),
            key.Exchange.RestBaseUrl);

        SpotAccountSnapshot? snap;
        try { snap = await spot.GetAccountAsync(cred, cancellationToken); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "spot account snapshot failed bot={BotId}", bot.Id);
            return;
        }
        var baseAsset = bot.Symbol.BaseAsset;
        var bal = snap.Balances.FirstOrDefault(b => b.Asset == baseAsset);
        var totalOwned = (bal?.Free ?? 0m) + (bal?.Locked ?? 0m);
        var tolerance = dbOpen.Quantity * 0.02m; // 2% tolerance for commission/dust
        if (totalOwned + tolerance < dbOpen.Quantity)
        {
            dbOpen.Status = PositionStatus.Closed;
            dbOpen.ClosedAt = DateTimeOffset.UtcNow;
            dbOpen.CloseReason ??= "reconciled_balance_missing";
            await db.SaveChangesAsync(cancellationToken);
            await botBus.PublishAsync(new BotEvent(bot.Id, "auto_close",
                $"LIVE SPOT position reconciled closed — balance {totalOwned:F6} {baseAsset} < expected {dbOpen.Quantity:F6}",
                DateTimeOffset.UtcNow), cancellationToken);
            logger.LogInformation("Spot reconciler closed bot={BotId} {Symbol}: owned={Owned} expected={Expected}",
                bot.Id, bot.Symbol.Code, totalOwned, dbOpen.Quantity);
        }
    }
}
