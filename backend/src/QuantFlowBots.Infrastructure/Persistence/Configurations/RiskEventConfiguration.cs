using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Configurations;

public sealed class RiskEventConfiguration : IEntityTypeConfiguration<RiskEvent>
{
    public void Configure(EntityTypeBuilder<RiskEvent> e)
    {
        e.ToTable("risk_events");
        e.HasKey(x => x.Id);
        e.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        e.Property(x => x.Severity).HasMaxLength(16).IsRequired();
        e.Property(x => x.Message).HasMaxLength(512).IsRequired();
        e.Property(x => x.ActionTaken).HasMaxLength(64);
        e.Property(x => x.ContextJson).HasColumnType("jsonb");
        e.HasOne(x => x.Bot).WithMany().HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
        e.HasIndex(x => new { x.BotId, x.CreatedAt });
    }
}
