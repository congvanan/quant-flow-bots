using QuantFlowBots.Application.Sentiment;

namespace QuantFlowBots.Infrastructure.Sentiment;

/// <summary>
/// Cheap, deterministic baseline: scan headline for bullish / bearish keyword hits,
/// normalize to [-1, 1]. Magnitude scales with number of hits (capped at 1.0).
/// Replaceable with an LLM-backed scorer later — no other code needs to change.
/// </summary>
public sealed class KeywordSentimentScorer : ISentimentScorer
{
    private static readonly string[] BullWords =
    [
        "surge", "rally", "soar", "moon", "pump", "breakout", "bull", "bullish",
        "approve", "approved", "etf", "adopt", "adoption", "partnership", "upgrade",
        "ath", "all-time high", "listing", "listed", "buy", "long", "milestone",
        "burn", "halving", "support", "win", "wins"
    ];

    private static readonly string[] BearWords =
    [
        "crash", "dump", "plunge", "tank", "bear", "bearish", "sell-off", "selloff",
        "hack", "exploit", "rug", "rugpull", "scam", "ban", "banned", "lawsuit",
        "sec", "delist", "delisted", "liquidation", "liquidations", "fud", "down",
        "fear", "fraud", "investigation", "fine", "outage"
    ];

    public ScoredSentiment Score(SentimentInput input)
    {
        var text = input.Headline.ToLowerInvariant();
        var bull = CountHits(text, BullWords);
        var bear = CountHits(text, BearWords);
        var net = bull - bear;
        var total = bull + bear;
        decimal score = total == 0 ? 0m : Math.Clamp((decimal)net / total, -1m, 1m);
        decimal magnitude = Math.Min(1.0m, total * 0.25m);
        if (total == 0) magnitude = 0.1m; // baseline weight even for neutral
        return new ScoredSentiment(
            input.SymbolCode.ToUpperInvariant(),
            input.Source,
            input.Headline,
            input.Url,
            score,
            magnitude,
            input.At,
            input.Tags);
    }

    private static int CountHits(string text, string[] words)
    {
        var hits = 0;
        foreach (var w in words)
            if (text.Contains(w, StringComparison.OrdinalIgnoreCase)) hits++;
        return hits;
    }
}
