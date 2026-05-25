using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Domain.Entities;

public sealed class Candle
{
    public DateTimeOffset OpenTime { get; set; }
    public int SymbolId { get; set; }
    public CandleInterval Interval { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal QuoteVolume { get; set; }
    public int TradeCount { get; set; }
    public DateTimeOffset CloseTime { get; set; }

    public Symbol? Symbol { get; set; }
}
