using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Configurations;

public sealed class CandleConfiguration : IEntityTypeConfiguration<Candle>
{
    public void Configure(EntityTypeBuilder<Candle> e)
    {
        e.ToTable("candles");
        e.HasKey(x => new { x.SymbolId, x.Interval, x.OpenTime });
        e.Property(x => x.Open).HasColumnType("numeric(28,12)");
        e.Property(x => x.High).HasColumnType("numeric(28,12)");
        e.Property(x => x.Low).HasColumnType("numeric(28,12)");
        e.Property(x => x.Close).HasColumnType("numeric(28,12)");
        e.Property(x => x.Volume).HasColumnType("numeric(28,12)");
        e.Property(x => x.QuoteVolume).HasColumnType("numeric(28,12)");
        e.HasOne(x => x.Symbol).WithMany().HasForeignKey(x => x.SymbolId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.SymbolId, x.Interval, x.OpenTime }).IsDescending(false, false, true);
    }
}
