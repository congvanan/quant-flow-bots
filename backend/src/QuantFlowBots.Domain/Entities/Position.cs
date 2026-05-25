using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Domain.Entities;

public sealed class Position
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BotId { get; set; }
    public Guid? BotRunId { get; set; }
    public int SymbolId { get; set; }
    public TradingMode Mode { get; set; }
    public PositionSide Side { get; set; }
    public PositionStatus Status { get; set; } = PositionStatus.Open;
    public decimal Quantity { get; set; }
    public decimal OriginalQuantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public decimal? TrailingStopPercent { get; set; }
    public decimal? HighestPriceSinceEntry { get; set; }
    public string? TakeProfitLevelsJson { get; set; }
    public bool BreakEvenTriggered { get; set; }
    public string? CloseReason { get; set; }
    public string? ExchangePositionRef { get; set; }
    public string? ExchangeStopOrderId { get; set; }
    public string? ExchangeTpOrderId { get; set; }
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }

    public Bot? Bot { get; set; }
    public BotRun? BotRun { get; set; }
    public Symbol? Symbol { get; set; }
}
