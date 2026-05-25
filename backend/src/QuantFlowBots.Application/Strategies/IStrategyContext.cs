using QuantFlowBots.Application.Exchanges;

namespace QuantFlowBots.Application.Strategies;

public interface IStrategyContext
{
    string Symbol { get; }
    DateTimeOffset Now { get; }
    IReadOnlyList<CandleData> History { get; }
    decimal? OpenPositionQuantity { get; }
    decimal? OpenPositionEntryPrice { get; }
    decimal Cash { get; }
}
