using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Configurations;

public sealed class SentimentEventConfiguration : IEntityTypeConfiguration<SentimentEvent>
{
    public void Configure(EntityTypeBuilder<SentimentEvent> e)
    {
        e.ToTable("sentiment_events");
        e.HasKey(x => x.Id);
        e.Property(x => x.SymbolCode).HasMaxLength(32).IsRequired();
        e.Property(x => x.Source).HasMaxLength(64).IsRequired();
        e.Property(x => x.Headline).HasMaxLength(512).IsRequired();
        e.Property(x => x.Url).HasMaxLength(1024);
        e.Property(x => x.Tags).HasMaxLength(256);
        e.Property(x => x.Score).HasColumnType("numeric(6,4)");
        e.Property(x => x.Magnitude).HasColumnType("numeric(6,4)");
        e.HasOne(x => x.Symbol).WithMany().HasForeignKey(x => x.SymbolId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.SymbolCode, x.At });
        e.HasIndex(x => x.IngestedAt);
    }
}
