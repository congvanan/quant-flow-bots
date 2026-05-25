using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Exchanges;

public interface IExchangeClient
{
    string ExchangeCode { get; }

    Task<IReadOnlyList<SymbolInfo>> GetSymbolsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TickerSnapshot>> GetAllTickersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TickerSnapshot>> GetRollingTickersAsync(
        IReadOnlyList<string> symbols,
        string windowSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CandleData>> GetCandlesAsync(
        string symbol,
        CandleInterval interval,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken cancellationToken);

    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken);

    Task<OrderResult> CancelOrderAsync(string symbol, string clientOrderId, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetSymbolListingDateAsync(string symbol, CancellationToken cancellationToken);
}
