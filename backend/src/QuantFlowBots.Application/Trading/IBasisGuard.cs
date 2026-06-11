namespace QuantFlowBots.Application.Trading;

/// <summary>
/// Basis guard: chặn entry khi spot vs futures lệch giá quá ngưỡng MaxBasisPct của bot.
/// Lý do (chốt 2026-06-03 — xem [[project-quantflow-market-axis]]): khi |spot − futures| / spot
/// vượt ngưỡng, thường có một trong các tình huống — futures long/short crowded, liquidation wick,
/// funding/basis nóng, entry dễ bị đuổi giá. Áp dụng cả khi same-market futures bot (chống wick trap).
/// </summary>
public interface IBasisGuard
{
    Task<BasisCheckResult> CheckAsync(string symbol, decimal maxBasisPct, CancellationToken ct);
}

/// <param name="Ok">True = pass (basis trong ngưỡng hoặc không đo được — fail-open để không khóa bot).</param>
/// <param name="BasisPct">|spot − futures| / spot × 100. 0 khi không đo được.</param>
/// <param name="Reason">null khi Ok; lý do block khi !Ok.</param>
public sealed record BasisCheckResult(bool Ok, decimal BasisPct, string? Reason)
{
    public static BasisCheckResult Pass(decimal basisPct) => new(true, basisPct, null);
    public static BasisCheckResult Block(decimal basisPct, string reason) => new(false, basisPct, reason);
    /// <summary>Không có data hai chiều — fail-open. Trade-off: bỏ qua guard còn hơn khóa bot khi cache cold.</summary>
    public static BasisCheckResult Unknown(string reason) => new(true, 0m, reason);
}
