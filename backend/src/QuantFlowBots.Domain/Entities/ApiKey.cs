using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Domain.Entities;

public sealed class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public int ExchangeId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string KeyPreview { get; set; } = string.Empty;
    public string EncryptedKey { get; set; } = string.Empty;
    public string EncryptedSecret { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "{}";
    public TradingMode Mode { get; set; } = TradingMode.Paper;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
    public Exchange? Exchange { get; set; }
}
