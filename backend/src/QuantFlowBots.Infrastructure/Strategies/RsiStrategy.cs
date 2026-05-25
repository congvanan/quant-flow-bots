using QuantFlowBots.Application.Exchanges;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Strategies;

public sealed class RsiStrategy : StrategyBase
{
    public const string KindCode = "rsi";

    private int _period = 14;
    private decimal _oversold = 30;
    private decimal _overbought = 70;

    public override string Kind => KindCode;
    public override int WarmupBars => _period + 1;

    public override void Configure(IReadOnlyDictionary<string, object?> p)
    {
        _period = Int(p, "period", 14);
        _oversold = Dec(p, "oversold", 30m);
        _overbought = Dec(p, "overbought", 70m);
        if (_oversold >= _overbought) throw new ArgumentException("oversold must be smaller than overbought");
    }

    public override StrategyDecision? OnCandle(CandleData candle, IStrategyContext context)
    {
        var history = context.History;
        if (history.Count < WarmupBars) return null;
        var closes = new decimal[history.Count];
        for (var i = 0; i < history.Count; i++) closes[i] = history[i].Close;
        var rsi = Indicators.Rsi(closes, _period);
        if (rsi is null) return null;

        var openQty = context.OpenPositionQuantity ?? 0m;
        if (rsi <= _oversold && openQty == 0)
            return new StrategyDecision(SignalType.Entry, OrderSide.Buy, candle.Close, (_oversold - rsi.Value) / _oversold,
                $"rsi={rsi:F2} <= {_oversold} oversold", new Dictionary<string, object?> { ["rsi"] = rsi });

        if (rsi >= _overbought && openQty > 0)
            return new StrategyDecision(SignalType.Exit, OrderSide.Sell, candle.Close, (rsi.Value - _overbought) / (100m - _overbought),
                $"rsi={rsi:F2} >= {_overbought} overbought", new Dictionary<string, object?> { ["rsi"] = rsi });

        return null;
    }
}
