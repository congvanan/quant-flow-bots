namespace QuantFlowBots.Domain.Entities;

/// <summary>
/// One scored opinion about a symbol. Source ranges from "manual" / "cryptopanic" / "news_rss" etc.
/// Score is [-1, 1]: negative = bearish, positive = bullish. Magnitude is a confidence/weight.
/// </summary>
public sealed class SentimentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? SymbolId { get; set; }
    public string SymbolCode { get; set; } = string.Empty;
    public string Source { get; set; } = "manual";
    public string Headline { get; set; } = string.Empty;
    public string? Url { get; set; }
    public decimal Score { get; set; }
    public decimal Magnitude { get; set; } = 1m;
    public string? Tags { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;

    public Symbol? Symbol { get; set; }
}
