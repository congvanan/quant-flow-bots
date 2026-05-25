using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Strategies;

public sealed record StrategyDecision(
    SignalType Type,
    OrderSide? Side,
    decimal? Price,
    decimal Score,
    string Reason,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public interface IStrategy
{
    string Kind { get; }
    int WarmupBars { get; }
    void Configure(IReadOnlyDictionary<string, object?> parameters);
    StrategyDecision? OnCandle(CandleData candle, IStrategyContext context);
}

public interface IStrategyFactory
{
    IStrategy Create(string kind);
    IEnumerable<string> AvailableKinds { get; }
}
