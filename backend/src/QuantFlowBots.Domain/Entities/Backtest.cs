using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Domain.Entities;

public sealed class Backtest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid StrategyId { get; set; }
    public int SymbolId { get; set; }
    public CandleInterval Interval { get; set; }
    public DateTimeOffset FromTime { get; set; }
    public DateTimeOffset ToTime { get; set; }
    public decimal InitialCapital { get; set; } = 10_000m;
    public decimal CommissionPercent { get; set; } = 0.1m;
    public string ParametersJson { get; set; } = "{}";
    public BacktestStatus Status { get; set; } = BacktestStatus.Queued;
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public User? User { get; set; }
    public Strategy? Strategy { get; set; }
    public Symbol? Symbol { get; set; }
}
