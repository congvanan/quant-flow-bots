using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Trading;

public sealed record PaperOrderRequest(
    Guid BotId,
    Guid? BotRunId,
    int SymbolId,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    string Reason,
    // Multi-account fan-out (xem [[project-quantflow-multi-account]]):
    //   ApiKeyId  = account thực thi lệnh con này. null = single-account legacy (dùng Bot.ApiKeyId).
    //   PositionId = đóng đúng vị thế này (close targeted theo account). null = tự tìm vị thế mở.
    Guid? ApiKeyId = null,
    Guid? PositionId = null);

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
