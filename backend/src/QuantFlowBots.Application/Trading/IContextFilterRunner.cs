using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Trading;

/// <summary>
/// Phase 3 (chốt 2026-06-03 — xem [[project-quantflow-market-axis]]): runtime evaluator cho
/// bộ lọc bối cảnh user chọn ở FE. Parse <c>ContextFiltersJson = {"filters":["spotTrend",...]}</c>
/// và chạy AND-logic — TẤT CẢ filter phải pass thì entry mới qua.
///
/// **Fail-open**: filter trả Unknown (cache cold, chưa implement) → coi như pass. Trade-off:
/// không khóa bot khi infra chưa sẵn sàng. Implement filter chưa có data → trả Unknown thay vì
/// crash hoặc block.
/// </summary>
public interface IContextFilterRunner
{
    Task<ContextFilterResult> CheckAsync(
        string? contextFiltersJson, string symbol, OrderSide side, CancellationToken ct);
}

public sealed record ContextFilterResult(bool Ok, string? BlockedBy, string? Reason)
{
    public static readonly ContextFilterResult Pass = new(true, null, null);
    public static ContextFilterResult Block(string filter, string reason) => new(false, filter, reason);
}
