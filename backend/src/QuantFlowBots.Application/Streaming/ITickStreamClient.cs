namespace QuantFlowBots.Application.Streaming;

public interface ITickStreamClient
{
    string ExchangeCode { get; }

    Task UpdateBookTickerSubscriptionsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken);
    Task UpdateAggTradeSubscriptionsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken);
    Task RunAsync(CancellationToken cancellationToken);
}
