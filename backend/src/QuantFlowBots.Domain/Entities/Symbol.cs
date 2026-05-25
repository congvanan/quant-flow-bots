namespace QuantFlowBots.Domain.Entities;

public sealed class Symbol
{
    public int Id { get; set; }
    public int ExchangeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;
    public decimal MinQuantity { get; set; }
    public decimal TickSize { get; set; }
    public decimal StepSize { get; set; }
    public decimal MinNotional { get; set; }
    public DateTimeOffset? FiltersUpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ListedAt { get; set; }

    public Exchange? Exchange { get; set; }
}
