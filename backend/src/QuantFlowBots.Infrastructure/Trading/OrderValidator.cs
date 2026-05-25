using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Infrastructure.Trading;

public sealed record OrderValidationResult(bool Approved, string? Reason, decimal AdjustedQuantity, decimal AdjustedPrice);

public static class OrderValidator
{
    /// <summary>
    /// Round <paramref name="quantity"/> down to <paramref name="symbol"/>.StepSize and
    /// round <paramref name="price"/> down to TickSize. Then validate MinQuantity and MinNotional.
    /// Returns the rounded values along with an approval flag.
    /// </summary>
    public static OrderValidationResult Validate(Symbol symbol, decimal quantity, decimal price)
    {
        var qty = RoundDownToStep(quantity, symbol.StepSize);
        var px = RoundDownToStep(price, symbol.TickSize);

        if (qty <= 0)
            return new OrderValidationResult(false, "qty_below_step", qty, px);

        if (symbol.MinQuantity > 0 && qty < symbol.MinQuantity)
            return new OrderValidationResult(false, $"qty<minQty ({qty}<{symbol.MinQuantity})", qty, px);

        if (symbol.MinNotional > 0)
        {
            var notional = qty * px;
            if (notional < symbol.MinNotional)
                return new OrderValidationResult(false, $"notional<minNotional ({notional:F2}<{symbol.MinNotional:F2})", qty, px);
        }

        return new OrderValidationResult(true, null, qty, px);
    }

    public static decimal RoundDownToStep(decimal value, decimal step)
    {
        if (step <= 0) return value;
        var n = Math.Floor(value / step);
        return n * step;
    }
}
