using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Trading;

public sealed record PaperOrderRequest(
    Guid BotId,
    Guid? BotRunId,
    int SymbolId,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    string Reason);

public sealed record PaperOrderResult(
    Guid OrderId,
    OrderStatus Status,
    decimal FilledQuantity,
    decimal AveragePrice,
    decimal RealizedPnl,
    string? PositionId);

public interface IPaperOrderExecutor
{
    Task<PaperOrderResult> ExecuteAsync(PaperOrderRequest request, CancellationToken cancellationToken);
}
