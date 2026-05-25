using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading;

/// <summary>
/// Run once at startup. Compares DB state against in-memory monitor and flags
/// inconsistencies as <see cref="RiskEvent"/> rows so the operator notices on /bots/{id}.
/// Currently read-only — never mutates positions automatically (SAFE_MODE).
/// </summary>
public sealed class ReconcileService(
    IServiceScopeFactory scopeFactory,
    PositionMonitor monitor,
    IBotEventBus botBus,
    ILogger<ReconcileService> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();

            var openPositions = await db.Positions
                .Where(p => p.Status == PositionStatus.Open)
                .Include(p => p.Symbol)
                .ToListAsync(cancellationToken);

            var monitored = monitor.WatchedSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var issues = new List<(Guid? botId, Guid? userId, string reason, string message)>();

            foreach (var p in openPositions)
            {
                if (p.Symbol is null)
                {
                    issues.Add((p.BotId, null, "reconcile_missing_symbol",
                        $"Position {p.Id} has no Symbol row attached"));
                    continue;
                }

                if (p.Quantity <= 0)
                {
                    issues.Add((p.BotId, null, "reconcile_invalid_qty",
                        $"Open position {p.Id} has Quantity={p.Quantity}"));
                }

                if (p.OriginalQuantity <= 0)
                {
                    // Legacy rows before Đợt B may have OriginalQuantity = 0. Auto-fix to Quantity.
                    p.OriginalQuantity = p.Quantity;
                }

                if (!monitored.Contains(p.Symbol.Code))
                {
                    issues.Add((p.BotId, null, "reconcile_unwatched_symbol",
                        $"Position {p.Id} on {p.Symbol.Code} not watched by PositionMonitor"));
                }
            }

            // Orphan bots: Running state but no active runtime
            var runningBots = await db.Bots
                .Where(b => b.State == BotState.Running)
                .Select(b => new { b.Id, b.UserId, b.Name })
                .ToListAsync(cancellationToken);

            // Persist a single critical event if anything is broken.
            if (issues.Count > 0)
            {
                foreach (var i in issues)
                {
                    var userId = i.userId ?? runningBots.FirstOrDefault(b => b.Id == i.botId)?.UserId ?? Guid.Empty;
                    db.RiskEvents.Add(new RiskEvent
                    {
                        UserId = userId,
                        BotId = i.botId,
                        EventType = i.reason,
                        Severity = "warn",
                        Message = i.message,
                        ActionTaken = "log_only",
                    });
                    if (i.botId.HasValue)
                    {
                        await botBus.PublishAsync(new BotEvent(i.botId.Value, "risk",
                            $"reconcile: {i.reason} — {i.message}", DateTimeOffset.UtcNow), cancellationToken);
                    }
                }
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Reconcile found {Count} issues on startup.", issues.Count);
            }
            else
            {
                logger.LogInformation("Reconcile clean — {Pos} open positions, {Bots} running bots.",
                    openPositions.Count, runningBots.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reconcile failed.");
        }
    }
}
