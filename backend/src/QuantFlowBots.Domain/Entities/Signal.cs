using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Domain.Entities;

public sealed class Signal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StrategyId { get; set; }
    public int SymbolId { get; set; }
    public SignalType Type { get; set; }
    public OrderSide? Side { get; set; }
    public decimal Price { get; set; }
    public decimal Score { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public Strategy? Strategy { get; set; }
    public Symbol? Symbol { get; set; }
}
