using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Backtesting;

public sealed record BacktestRequest(
    Guid BacktestId,
    Guid StrategyId,
    int SymbolId,
    CandleInterval Interval,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal InitialCapital,
    decimal CommissionPercent,
    string ParametersJson,
    // Market axis: Spot = long-only + nến spot api (behavior cũ). Futures = long+short,
    // nến fapi, margin model + leverage. Default Spot để caller cũ không đổi.
    MarketKind Market = MarketKind.Spot,
    decimal Leverage = 1m);

public sealed record BacktestMetrics(
    decimal FinalEquity,
    decimal TotalReturnPercent,
    decimal MaxDrawdownPercent,
    decimal SharpeRatio,
    int TradeCount,
    int WinCount,
    decimal WinRatePercent,
    IReadOnlyList<EquityPoint> EquityCurve);

public sealed record EquityPoint(DateTimeOffset At, decimal Equity);

public interface IBacktestRunner
{
    Task<BacktestMetrics> RunAsync(BacktestRequest request, CancellationToken cancellationToken);
}
