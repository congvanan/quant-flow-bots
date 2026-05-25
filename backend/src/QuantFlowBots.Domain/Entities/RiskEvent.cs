namespace QuantFlowBots.Domain.Entities;

public sealed class RiskEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? BotId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public string? ActionTaken { get; set; }
    public string? ContextJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Bot? Bot { get; set; }
}
