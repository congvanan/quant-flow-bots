using QuantFlowBots.Application.Sentiment;
using QuantFlowBots.Application.Strategies;

namespace QuantFlowBots.Infrastructure.Strategies;

public sealed class StrategyFactory(ISentimentAggregator sentiment) : IStrategyFactory
{
    public IEnumerable<string> AvailableKinds =>
        [SmaCrossStrategy.KindCode, RsiStrategy.KindCode, BreakoutStrategy.KindCode, SentimentMomentumStrategy.KindCode];

    public IStrategy Create(string kind) => kind switch
    {
        SmaCrossStrategy.KindCode => new SmaCrossStrategy(),
        RsiStrategy.KindCode => new RsiStrategy(),
        BreakoutStrategy.KindCode => new BreakoutStrategy(),
        SentimentMomentumStrategy.KindCode => new SentimentMomentumStrategy(sentiment),
        _ => throw new ArgumentException($"Unknown strategy kind: {kind}")
    };
}
