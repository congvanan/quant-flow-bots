using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Trading;

/// <summary>
/// Routes order placement to the correct backend (paper vs live futures testnet)
/// based on the bot's current RunMode. Callers must NOT call IPaperOrderExecutor directly.
/// </summary>
public interface ITradingDispatcher
{
    Task<PaperOrderResult> ExecuteAsync(PaperOrderRequest request, CancellationToken cancellationToken);
}

public sealed record LiveTradingGateResult(bool Allowed, string? Reason);

public interface ILiveTradingGate
{
    Task<LiveTradingGateResult> EvaluateAsync(Guid botId, CancellationToken cancellationToken);
}
