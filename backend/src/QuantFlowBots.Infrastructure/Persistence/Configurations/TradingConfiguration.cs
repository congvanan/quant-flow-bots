using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> e)
    {
        e.ToTable("orders");
        e.HasKey(x => x.Id);
        e.Property(x => x.Price).HasColumnType("numeric(28,12)");
        e.Property(x => x.Quantity).HasColumnType("numeric(28,12)");
        e.Property(x => x.FilledQuantity).HasColumnType("numeric(28,12)");
        e.Property(x => x.AveragePrice).HasColumnType("numeric(28,12)");
        e.Property(x => x.Commission).HasColumnType("numeric(28,12)");
        e.Property(x => x.ClientOrderId).HasMaxLength(64).IsRequired();
        e.Property(x => x.ExchangeOrderId).HasMaxLength(64);
        e.HasOne(x => x.Bot).WithMany().HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.BotRun).WithMany().HasForeignKey(x => x.BotRunId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.Symbol).WithMany().HasForeignKey(x => x.SymbolId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.ClientOrderId).IsUnique();
        e.HasIndex(x => new { x.BotId, x.CreatedAt });
        e.HasIndex(x => x.ApiKeyId);
    }
}

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> e)
    {
        e.ToTable("positions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Quantity).HasColumnType("numeric(28,12)");
        e.Property(x => x.OriginalQuantity).HasColumnType("numeric(28,12)");
        e.Property(x => x.EntryPrice).HasColumnType("numeric(28,12)");
        e.Property(x => x.ExitPrice).HasColumnType("numeric(28,12)");
        e.Property(x => x.RealizedPnl).HasColumnType("numeric(28,12)");
        e.Property(x => x.StopLossPrice).HasColumnType("numeric(28,12)");
        e.Property(x => x.TakeProfitPrice).HasColumnType("numeric(28,12)");
        e.Property(x => x.TrailingStopPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.HighestPriceSinceEntry).HasColumnType("numeric(28,12)");
        e.Property(x => x.TakeProfitLevelsJson).HasColumnType("jsonb");
        e.Property(x => x.CloseReason).HasMaxLength(64);
        e.Property(x => x.ExchangePositionRef).HasMaxLength(64);
        e.Property(x => x.ExchangeStopOrderId).HasMaxLength(64);
        e.Property(x => x.ExchangeTpOrderId).HasMaxLength(64);
        e.HasOne(x => x.Bot).WithMany().HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.BotRun).WithMany().HasForeignKey(x => x.BotRunId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.Symbol).WithMany().HasForeignKey(x => x.SymbolId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.BotId, x.Status });
        e.HasIndex(x => new { x.ApiKeyId, x.Status });
    }
}

public sealed class SignalConfiguration : IEntityTypeConfiguration<Signal>
{
    public void Configure(EntityTypeBuilder<Signal> e)
    {
        e.ToTable("signals");
        e.HasKey(x => x.Id);
        e.Property(x => x.Price).HasColumnType("numeric(28,12)");
        e.Property(x => x.Score).HasColumnType("numeric(10,4)");
        e.Property(x => x.PayloadJson).HasColumnType("jsonb");
        e.HasOne(x => x.Strategy).WithMany().HasForeignKey(x => x.StrategyId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Symbol).WithMany().HasForeignKey(x => x.SymbolId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StrategyId, x.GeneratedAt });
    }
}

public sealed class BacktestConfiguration : IEntityTypeConfiguration<Backtest>
{
    public void Configure(EntityTypeBuilder<Backtest> e)
    {
        e.ToTable("backtests");
        e.HasKey(x => x.Id);
        e.Property(x => x.InitialCapital).HasColumnType("numeric(28,12)");
        e.Property(x => x.CommissionPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.ParametersJson).HasColumnType("jsonb");
        e.Property(x => x.ResultJson).HasColumnType("jsonb");
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Strategy).WithMany().HasForeignKey(x => x.StrategyId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Symbol).WithMany().HasForeignKey(x => x.SymbolId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
