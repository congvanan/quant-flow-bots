using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Trading;

public sealed record RiskCheckRequest(
    Guid BotId,
    Guid UserId,
    int SymbolId,
    OrderSide Side,
    decimal Price);

public sealed record RiskCheckResult(
    bool Approved,
    string? Reason,
    decimal Quantity,
    decimal? StopLossPrice,
    decimal? TakeProfitPrice);

public interface IRiskEngine
{
    Task<RiskCheckResult> EvaluateAsync(RiskCheckRequest request, CancellationToken cancellationToken);

    Task<bool> TripKillSwitchAsync(Guid botId, string reason, CancellationToken cancellationToken);

    Task<bool> ResetKillSwitchAsync(Guid botId, CancellationToken cancellationToken);
}
