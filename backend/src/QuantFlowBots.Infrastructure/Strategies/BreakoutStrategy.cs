using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Strategies;

public sealed class BreakoutStrategy : StrategyBase
{
    public const string KindCode = "breakout";

    private int _lookback = 20;
    private decimal _buffer = 0.001m;

    public override string Kind => KindCode;
    public override int WarmupBars => _lookback + 1;

    public override void Configure(IReadOnlyDictionary<string, object?> p)
    {
        _lookback = Int(p, "lookback", 20);
        _buffer = Dec(p, "buffer", 0.001m);
    }

    public override StrategyDecision? OnCandle(CandleData candle, IStrategyContext context)
    {
        var history = context.History;
        if (history.Count < WarmupBars) return null;

        var highs = new decimal[history.Count - 1];
        var lows = new decimal[history.Count - 1];
        for (var i = 0; i < history.Count - 1; i++) { highs[i] = history[i].High; lows[i] = history[i].Low; }
        var hl = Indicators.HighLow(highs, lows, _lookback);
        if (hl is null) return null;

        var (refHigh, refLow) = hl.Value;
        var upTrigger = refHigh * (1m + _buffer);
        var downTrigger = refLow * (1m - _buffer);
        var openQty = context.OpenPositionQuantity ?? 0m;

        if (candle.Close > upTrigger && openQty == 0)
            return new StrategyDecision(SignalType.Entry, OrderSide.Buy, candle.Close, 1m,
                $"close>{upTrigger:F4} (lookback {_lookback} high)", new Dictionary<string, object?> { ["refHigh"] = refHigh });

        if (candle.Close < downTrigger && openQty > 0)
            return new StrategyDecision(SignalType.Exit, OrderSide.Sell, candle.Close, 1m,
                $"close<{downTrigger:F4} (lookback {_lookback} low)", new Dictionary<string, object?> { ["refLow"] = refLow });

        return null;
    }
}
