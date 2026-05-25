using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Configurations;

public sealed class ExchangeConfiguration : IEntityTypeConfiguration<Exchange>
{
    public void Configure(EntityTypeBuilder<Exchange> e)
    {
        e.ToTable("exchanges");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(32).IsRequired();
        e.Property(x => x.Name).HasMaxLength(128).IsRequired();
        e.Property(x => x.RestBaseUrl).HasMaxLength(256).IsRequired();
        e.Property(x => x.WebSocketBaseUrl).HasMaxLength(256).IsRequired();
        e.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> e)
    {
        e.ToTable("api_keys");
        e.HasKey(x => x.Id);
        e.Property(x => x.Label).HasMaxLength(128).IsRequired();
        e.Property(x => x.KeyPreview).HasMaxLength(32).IsRequired();
        e.Property(x => x.EncryptedKey).IsRequired();
        e.Property(x => x.EncryptedSecret).IsRequired();
        e.Property(x => x.PermissionsJson).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.LastError).HasMaxLength(512);
        e.HasOne(x => x.User).WithMany(u => u.ApiKeys).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Exchange).WithMany().HasForeignKey(x => x.ExchangeId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.UserId, x.ExchangeId, x.Label }).IsUnique();
        e.HasIndex(x => new { x.UserId, x.ExchangeId, x.Mode, x.IsActive });
    }
}

public sealed class SymbolConfiguration : IEntityTypeConfiguration<Symbol>
{
    public void Configure(EntityTypeBuilder<Symbol> e)
    {
        e.ToTable("symbols");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(32).IsRequired();
        e.Property(x => x.BaseAsset).HasMaxLength(16).IsRequired();
        e.Property(x => x.QuoteAsset).HasMaxLength(16).IsRequired();
        e.Property(x => x.MinQuantity).HasColumnType("numeric(28,12)");
        e.Property(x => x.TickSize).HasColumnType("numeric(28,12)");
        e.Property(x => x.StepSize).HasColumnType("numeric(28,12)");
        e.HasOne(x => x.Exchange).WithMany(x => x.Symbols).HasForeignKey(x => x.ExchangeId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ExchangeId, x.Code }).IsUnique();
        e.HasIndex(x => x.ListedAt);
    }
}
