using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Persistence.Configurations;

public sealed class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> e)
    {
        e.ToTable("strategies");
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(128).IsRequired();
        e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        e.Property(x => x.ParametersJson).HasColumnType("jsonb");
        e.HasOne(x => x.User).WithMany(u => u.Strategies).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.UserId, x.Name });
    }
}

public sealed class BotConfiguration : IEntityTypeConfiguration<Bot>
{
    public void Configure(EntityTypeBuilder<Bot> e)
    {
        e.ToTable("bots");
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(128).IsRequired();
        e.Property(x => x.BaseEquityUsdt).HasColumnType("numeric(28,12)");
        e.Property(x => x.MaxPositionSize).HasColumnType("numeric(28,12)");
        e.Property(x => x.RiskPerTradePercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.DailyLossStopPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.DefaultStopLossPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.DefaultTakeProfitPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.DefaultTrailingStopPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.AtrMultiplier).HasColumnType("numeric(8,4)");
        e.Property(x => x.BreakEvenTriggerPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.BreakEvenOffsetPercent).HasColumnType("numeric(8,4)");
        e.Property(x => x.TakeProfitLevelsJson).HasColumnType("jsonb");
        e.Property(x => x.SymbolFilterJson).HasColumnType("jsonb");
        e.Property(x => x.StopLossKind).HasConversion<int>();
        e.Property(x => x.RunMode).HasConversion<int>();
        e.Property(x => x.Kind).HasConversion<int>();
        e.Property(x => x.KindConfigJson).HasColumnType("jsonb");
        e.Property(x => x.ExecutionMarket).HasConversion<int>();
        e.Property(x => x.TriggerMarket).HasConversion<int>();
        e.Property(x => x.ContextFiltersJson).HasColumnType("jsonb");
        e.Property(x => x.MaxBasisPct).HasColumnType("numeric(8,4)");
        e.Property(x => x.KillSwitchReason).HasMaxLength(256);
        e.HasOne(x => x.User).WithMany(u => u.Bots).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Strategy).WithMany().HasForeignKey(x => x.StrategyId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Symbol).WithMany().HasForeignKey(x => x.SymbolId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Exchange).WithMany().HasForeignKey(x => x.ExchangeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApiKey>().WithMany().HasForeignKey(x => x.ApiKeyId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        e.HasIndex(x => new { x.UserId, x.State });
    }
}

public sealed class BotRunConfiguration : IEntityTypeConfiguration<BotRun>
{
    public void Configure(EntityTypeBuilder<BotRun> e)
    {
        e.ToTable("bot_runs");
        e.HasKey(x => x.Id);
        e.Property(x => x.RealizedPnl).HasColumnType("numeric(28,12)");
        e.HasOne(x => x.Bot).WithMany().HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.BotId, x.StartedAt });
    }
}
