using System.Threading.Channels;

namespace QuantFlowBots.Application.Streaming;

public interface IMarketEventBus
{
    ValueTask PublishAsync(MarketEvent evt, CancellationToken cancellationToken);
    ChannelReader<TickerEvent> Tickers { get; }
    ChannelReader<KlineEvent> Klines { get; }
}

public interface ISignalEventBus
{
    ValueTask PublishAsync(SignalEvent evt, CancellationToken cancellationToken);
    ChannelReader<SignalEvent> Signals { get; }
}

public interface IBotEventBus
{
    ValueTask PublishAsync(BotEvent evt, CancellationToken cancellationToken);
    ChannelReader<BotEvent> Events { get; }
}

public interface IVolumeSpikeBus
{
    ValueTask PublishAsync(VolumeSpikeEvent evt, CancellationToken cancellationToken);
    ChannelReader<VolumeSpikeEvent> Spikes { get; }
}

public interface IOrderBookWallBus
{
    ValueTask PublishAsync(OrderBookWallEvent evt, CancellationToken cancellationToken);
    ChannelReader<OrderBookWallEvent> Walls { get; }
}

public interface ITickStreamBus
{
    ValueTask PublishBookTickerAsync(BookTickerEvent evt, CancellationToken cancellationToken);
    ValueTask PublishAggTradeAsync(AggTradeEvent evt, CancellationToken cancellationToken);
    ChannelReader<BookTickerEvent> BookTickers { get; }
    ChannelReader<AggTradeEvent> AggTrades { get; }
}
