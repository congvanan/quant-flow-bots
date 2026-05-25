using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Strategies;

public sealed class SmaCrossStrategy : StrategyBase
{
    public const string KindCode = "sma_cross";

    private int _fast = 9;
    private int _slow = 21;

    public override string Kind => KindCode;
    public override int WarmupBars => Math.Max(_fast, _slow) + 1;

    public override void Configure(IReadOnlyDictionary<string, object?> p)
    {
        _fast = Int(p, "fast", 9);
        _slow = Int(p, "slow", 21);
        if (_fast >= _slow) throw new ArgumentException("fast must be smaller than slow");
    }

    public override StrategyDecision? OnCandle(CandleData candle, IStrategyContext context)
    {
        var history = context.History;
        if (history.Count < WarmupBars + 1) return null;

        var closes = new decimal[history.Count];
        for (var i = 0; i < history.Count; i++) closes[i] = history[i].Close;

        var fastPrev = Indicators.Sma(closes[..^1], _fast);
        var slowPrev = Indicators.Sma(closes[..^1], _slow);
        var fastNow = Indicators.Sma(closes, _fast);
        var slowNow = Indicators.Sma(closes, _slow);
        if (fastPrev is null || slowPrev is null || fastNow is null || slowNow is null) return null;

        var crossedUp = fastPrev <= slowPrev && fastNow > slowNow;
        var crossedDown = fastPrev >= slowPrev && fastNow < slowNow;
        var openQty = context.OpenPositionQuantity ?? 0m;

        if (crossedUp && openQty == 0)
            return new StrategyDecision(SignalType.Entry, OrderSide.Buy, candle.Close, 1m,
                $"sma{_fast}>{_slow} cross up", new Dictionary<string, object?> { ["fast"] = fastNow, ["slow"] = slowNow });

        if (crossedDown && openQty > 0)
            return new StrategyDecision(SignalType.Exit, OrderSide.Sell, candle.Close, 1m,
                $"sma{_fast}<{_slow} cross down", new Dictionary<string, object?> { ["fast"] = fastNow, ["slow"] = slowNow });

        return null;
    }
}
