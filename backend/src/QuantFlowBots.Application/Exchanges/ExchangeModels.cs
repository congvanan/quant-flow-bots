using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Exchanges;

public sealed record SymbolInfo(
    string Code,
    string BaseAsset,
    string QuoteAsset,
    decimal MinQuantity,
    decimal TickSize,
    decimal StepSize,
    decimal MinNotional = 0m);

public sealed record TickerSnapshot(
    string Symbol,
    decimal LastPrice,
    decimal PriceChangePercent,
    decimal QuoteVolume,
    DateTimeOffset At);

public sealed record CandleData(
    string Symbol,
    CandleInterval Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    bool IsClosed,
    decimal TakerBuyQuoteVolume = 0m);

public sealed record OrderRequest(
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price,
    string ClientOrderId);

public sealed record OrderResult(
    string ClientOrderId,
    string? ExchangeOrderId,
    OrderStatus Status,
    decimal Price,
    decimal Quantity,
    decimal FilledQuantity,
    decimal AveragePrice,
    decimal Commission,
    string? RejectReason);
