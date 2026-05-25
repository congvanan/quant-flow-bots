using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Domain.Entities;

public sealed class Bot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid StrategyId { get; set; }
    public int SymbolId { get; set; }
    public int ExchangeId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public int Leverage { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public TradingMode Mode { get; set; } = TradingMode.Paper;
    public BotState State { get; set; } = BotState.Stopped;
    public BotRunMode RunMode { get; set; } = BotRunMode.PaperTrading;
    public BotKind Kind { get; set; } = BotKind.Signal;
    public string? KindConfigJson { get; set; }
    public string? SymbolFilterJson { get; set; }

    // Capital / sizing
    public decimal BaseEquityUsdt { get; set; } = 1000m;
    public decimal MaxPositionSize { get; set; }
    public decimal? RiskPerTradePercent { get; set; }
    public int MaxOpenPositions { get; set; } = 1;

    // SL/TP/Trailing defaults
    public StopLossKind StopLossKind { get; set; } = StopLossKind.FixedPercent;
    public decimal? DefaultStopLossPercent { get; set; }
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrMultiplier { get; set; } = 1.5m;
    public decimal? DefaultTakeProfitPercent { get; set; }
    public string? TakeProfitLevelsJson { get; set; }
    public decimal? DefaultTrailingStopPercent { get; set; }
    public bool BreakEvenEnabled { get; set; }
    public decimal? BreakEvenTriggerPercent { get; set; }
    public decimal BreakEvenOffsetPercent { get; set; } = 0.1m;

    // Risk Engine config
    public decimal DailyLossStopPercent { get; set; } = 4m;
    public int MaxConsecutiveLosses { get; set; } = 0;
    public int CooldownAfterLossMinutes { get; set; } = 0;
    public bool KillSwitchEnabled { get; set; } = true;
    public DateTimeOffset? KillSwitchTrippedAt { get; set; }
    public string? KillSwitchReason { get; set; }

    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
    public Strategy? Strategy { get; set; }
    public Symbol? Symbol { get; set; }
    public Exchange? Exchange { get; set; }
}
