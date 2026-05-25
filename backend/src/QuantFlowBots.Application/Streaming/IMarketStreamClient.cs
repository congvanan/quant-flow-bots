using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Application.Streaming;

public interface IMarketStreamClient
{
    string ExchangeCode { get; }

    Task SubscribeTickersAsync(IEnumerable<string> symbols, CancellationToken cancellationToken);

    Task SubscribeKlinesAsync(IEnumerable<string> symbols, CandleInterval interval, CancellationToken cancellationToken);

    Task RunAsync(CancellationToken cancellationToken);
}
