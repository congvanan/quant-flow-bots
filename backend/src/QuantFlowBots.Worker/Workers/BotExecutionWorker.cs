using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Trading;

namespace QuantFlowBots.Worker.Workers;

public sealed class BotExecutionWorker(
    ISignalEventBus signalBus,
    IBotEventBus botBus,
    BotRuntime runtime,
    IServiceScopeFactory scopeFactory,
    ILogger<BotExecutionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BotExecutionWorker started.");
        await foreach (var sig in signalBus.Signals.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(sig, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BotExecution failed for signal {SignalId}", sig.SignalId);
            }
        }
    }

    private async Task HandleAsync(SignalEvent sig, CancellationToken cancellationToken)
    {
        if (sig.Side is null) return;
        if (!Enum.TryParse<OrderSide>(sig.Side, true, out var side)) return;

        var activeBot = runtime.Active.Values.FirstOrDefault(b =>
            string.Equals(b.SymbolCode, sig.Symbol, StringComparison.OrdinalIgnoreCase));
        if (activeBot is null) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantFlowBotsDbContext>();
        var botRow = await db.Bots.FirstOrDefaultAsync(b => b.Id == activeBot.BotId, cancellationToken);
        if (botRow is null || botRow.State != BotState.Running) return;

        // Honor RunMode before involving RiskEngine (avoid spamming risk_events for ScanOnly).
        if (botRow.RunMode is BotRunMode.Off or BotRunMode.ScanOnly)
        {
            await botBus.PublishAsync(new BotEvent(activeBot.BotId, "signal",
                $"signal {side} skipped — RunMode={botRow.RunMode}", DateTimeOffset.UtcNow), cancellationToken);
            return;
        }

        var risk = scope.ServiceProvider.GetRequiredService<IRiskEngine>();
        var check = await risk.EvaluateAsync(new RiskCheckRequest(
            activeBot.BotId, activeBot.UserId, activeBot.SymbolId, side, sig.Price), cancellationToken);
        if (!check.Approved)
        {
            await botBus.PublishAsync(new BotEvent(activeBot.BotId, "order",
                $"order {side} blocked by risk: {check.Reason}", DateTimeOffset.UtcNow), cancellationToken);
            logger.LogInformation("Bot {BotId}: blocked {Side} — {Reason}", activeBot.BotId, side, check.Reason);
            return;
        }

        var dispatcher = scope.ServiceProvider.GetRequiredService<ITradingDispatcher>();
        var result = await dispatcher.ExecuteAsync(new PaperOrderRequest(
            activeBot.BotId, null, activeBot.SymbolId, side, check.Quantity, sig.Price, $"signal:{sig.SignalId}"), cancellationToken);

        var message = result.Status == OrderStatus.Filled
            ? $"order {side} qty={check.Quantity:F6} @ {sig.Price:F4} filled. pnl={result.RealizedPnl:F2}"
            : $"order {side} rejected.";
        await botBus.PublishAsync(new BotEvent(activeBot.BotId, "order", message, DateTimeOffset.UtcNow), cancellationToken);
        logger.LogInformation("Bot {BotId}: {Message}", activeBot.BotId, message);
    }
}
