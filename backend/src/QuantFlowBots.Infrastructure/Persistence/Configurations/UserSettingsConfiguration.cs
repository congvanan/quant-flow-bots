using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Configurations;

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> e)
    {
        e.ToTable("user_settings");
        e.HasKey(x => x.UserId);
        e.Property(x => x.TelegramBotToken).HasMaxLength(256);
        e.Property(x => x.TelegramChatId).HasMaxLength(64);
    }
}
