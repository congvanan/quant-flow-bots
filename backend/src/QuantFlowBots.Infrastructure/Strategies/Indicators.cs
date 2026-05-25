namespace QuantFlowBots.Infrastructure.Strategies;

internal static class Indicators
{
    public static decimal? Sma(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count < period) return null;
        decimal sum = 0;
        for (var i = values.Count - period; i < values.Count; i++) sum += values[i];
        return sum / period;
    }

    public static decimal? Rsi(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return null;
        decimal gain = 0, loss = 0;
        for (var i = closes.Count - period; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff > 0) gain += diff; else loss += -diff;
        }
        if (loss == 0) return 100m;
        var rs = gain / loss;
        return 100m - 100m / (1 + rs);
    }

    public static decimal? Atr(IReadOnlyList<decimal> highs, IReadOnlyList<decimal> lows, IReadOnlyList<decimal> closes, int period)
    {
        if (period <= 0 || highs.Count <= period || lows.Count <= period || closes.Count <= period) return null;
        decimal sum = 0;
        for (var i = highs.Count - period; i < highs.Count; i++)
        {
            var prevClose = closes[i - 1];
            var tr = Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - prevClose), Math.Abs(lows[i] - prevClose)));
            sum += tr;
        }
        return sum / period;
    }

    public static (decimal high, decimal low)? HighLow(IReadOnlyList<decimal> highs, IReadOnlyList<decimal> lows, int period)
    {
        if (highs.Count < period || lows.Count < period) return null;
        var hi = highs[highs.Count - period];
        var lo = lows[lows.Count - period];
        for (var i = highs.Count - period; i < highs.Count; i++)
        {
            if (highs[i] > hi) hi = highs[i];
            if (lows[i] < lo) lo = lows[i];
        }
        return (hi, lo);
    }
}
