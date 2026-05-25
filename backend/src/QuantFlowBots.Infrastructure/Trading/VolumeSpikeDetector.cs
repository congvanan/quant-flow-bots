using Microsoft.Extensions.Logging;
using QuantFlowBots.Application.Streaming;

namespace QuantFlowBots.Infrastructure.Trading;

public sealed class VolumeSpikeDetector(VolumeSpikeCache cache, ILogger<VolumeSpikeDetector> logger)
{
    public const decimal MinQuoteVolume = 200_000m;
    public const decimal VolumeMultiplier = 3.0m;
    public const decimal BuyRatioThreshold = 0.60m;
    public const decimal SellRatioThreshold = 0.40m;
    private const int RequiredHistory = 20;

    public VolumeSpikeEvent? Process(KlineEvent evt)
    {
        if (!evt.Candle.IsClosed) return null;
        if (!evt.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)) return null;

        var buffer = cache.GetOrCreateBuffer(evt.Symbol);
        buffer.Add(evt.Candle);

        var history = buffer.Snapshot();
        if (history.Count < RequiredHistory + 1) return null;

        var current = history[^1];
        if (current.QuoteVolume < MinQuoteVolume) return null;
        if (current.Volume <= 0) return null;

        var prior = history.Take(history.Count - 1).TakeLast(RequiredHistory).ToList();
        var avg = prior.Average(c => c.QuoteVolume);
        if (avg <= 0) return null;

        var multiplier = current.QuoteVolume / avg;
        if (multiplier < VolumeMultiplier) return null;

        var takerRatio = current.QuoteVolume == 0 ? 0m : current.TakerBuyQuoteVolume / current.QuoteVolume;
        string direction;
        if (takerRatio >= BuyRatioThreshold) direction = "Buy";
        else if (takerRatio <= SellRatioThreshold) direction = "Sell";
        else return null;

        var pct5m = ComputePctChange(prior, current);
        var sparkline = history.TakeLast(20).Select(c => c.Close).ToList();

        var spike = new VolumeSpikeEvent(
            Symbol: evt.Symbol,
            Direction: direction,
            Price: current.Close,
            PriceChange5mPercent: pct5m,
            QuoteVolume: current.QuoteVolume,
            AverageQuoteVolume: avg,
            Multiplier: multiplier,
            TakerBuyRatio: takerRatio,
            Sparkline: sparkline,
            At: evt.At);

        cache.Add(spike);
        logger.LogInformation("Volume spike: {Sym} {Dir} qty={Qty:F0} USDT mult={Mult:F1}x ratio={Ratio:F2}",
            spike.Symbol, spike.Direction, spike.QuoteVolume, spike.Multiplier, spike.TakerBuyRatio);
        return spike;
    }

    private static decimal ComputePctChange(IReadOnlyList<Application.Exchanges.CandleData> prior, Application.Exchanges.CandleData current)
    {
        if (prior.Count < 5) return 0m;
        var ref5m = prior[^5].Open;
        if (ref5m == 0) return 0m;
        return (current.Close - ref5m) / ref5m * 100m;
    }
}
