using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Sentiment;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Strategies;

/// <summary>
/// Trade with the sentiment tide. Long when rolling sentiment for the symbol is
/// strongly positive and there's no open position. Exit when sentiment flips
/// strongly negative. Reads the live aggregator — same instance that the manual /
/// ingestion pipeline feeds.
/// </summary>
public sealed class SentimentMomentumStrategy(ISentimentAggregator sentiment) : StrategyBase
{
    public const string KindCode = "sentiment_momentum";

    private decimal _enter = 0.4m;
    private decimal _exit = -0.2m;
    private int _minSamples = 3;
    private decimal _minMagnitude = 0.2m;

    public override string Kind => KindCode;
    public override int WarmupBars => 1;

    public override void Configure(IReadOnlyDictionary<string, object?> p)
    {
        _enter = Dec(p, "enter", 0.4m);
        _exit = Dec(p, "exit", -0.2m);
        _minSamples = Int(p, "minSamples", 3);
        _minMagnitude = Dec(p, "minMagnitude", 0.2m);
        if (_exit >= _enter) throw new ArgumentException("exit must be smaller than enter");
    }

    public override StrategyDecision? OnCandle(CandleData candle, IStrategyContext context)
    {
        var snap = sentiment.Get(context.Symbol);
        if (snap.SampleCount < _minSamples || snap.RollingMagnitude < _minMagnitude) return null;

        var openQty = context.OpenPositionQuantity ?? 0m;

        if (snap.RollingScore >= _enter && openQty == 0)
            return new StrategyDecision(SignalType.Entry, OrderSide.Buy, candle.Close,
                Math.Min(1m, snap.RollingScore),
                $"sentiment={snap.RollingScore:F2} ≥ {_enter} (n={snap.SampleCount}, mag={snap.RollingMagnitude:F2})",
                new Dictionary<string, object?>
                {
                    ["score"] = snap.RollingScore,
                    ["magnitude"] = snap.RollingMagnitude,
                    ["samples"] = snap.SampleCount,
                });

        if (snap.RollingScore <= _exit && openQty > 0)
            return new StrategyDecision(SignalType.Exit, OrderSide.Sell, candle.Close,
                Math.Min(1m, Math.Abs(snap.RollingScore)),
                $"sentiment={snap.RollingScore:F2} ≤ {_exit} (n={snap.SampleCount})",
                new Dictionary<string, object?> { ["score"] = snap.RollingScore });

        return null;
    }
}
