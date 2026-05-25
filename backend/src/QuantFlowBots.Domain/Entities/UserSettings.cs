namespace QuantFlowBots.Domain.Entities;

public sealed class UserSettings
{
    public Guid UserId { get; set; }
    public string? TelegramBotToken { get; set; }
    public string? TelegramChatId { get; set; }
    public bool TelegramAlertsEnabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
