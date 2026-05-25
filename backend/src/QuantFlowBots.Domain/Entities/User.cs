using Microsoft.AspNetCore.Identity;

namespace QuantFlowBots.Domain.Entities;

public sealed class User : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
    public ICollection<Strategy> Strategies { get; set; } = new List<Strategy>();
    public ICollection<Bot> Bots { get; set; } = new List<Bot>();
}
