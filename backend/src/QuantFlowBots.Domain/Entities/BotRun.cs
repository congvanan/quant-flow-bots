namespace QuantFlowBots.Domain.Entities;

public sealed class BotRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BotId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StoppedAt { get; set; }
    public string? StopReason { get; set; }
    public decimal RealizedPnl { get; set; }
    public int OrderCount { get; set; }

    public Bot? Bot { get; set; }
}
