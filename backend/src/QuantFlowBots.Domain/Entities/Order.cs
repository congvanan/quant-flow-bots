using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Domain.Entities;

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BotId { get; set; }
    public Guid? BotRunId { get; set; }
    public int SymbolId { get; set; }
    public TradingMode Mode { get; set; }
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal Commission { get; set; }
    public string ClientOrderId { get; set; } = string.Empty;
    public string? ExchangeOrderId { get; set; }
    public string? RejectReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FilledAt { get; set; }

    public Bot? Bot { get; set; }
    public BotRun? BotRun { get; set; }
    public Symbol? Symbol { get; set; }
}
