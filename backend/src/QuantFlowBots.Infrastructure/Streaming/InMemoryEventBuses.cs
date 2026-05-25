using System.Threading.Channels;
using QuantFlowBots.Application.Streaming;

namespace QuantFlowBots.Infrastructure.Streaming;

public sealed class InMemoryMarketEventBus : IMarketEventBus
{
    private readonly Channel<TickerEvent> _tickers = Channel.CreateBounded<TickerEvent>(
        new BoundedChannelOptions(8192) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = false });

    private readonly Channel<KlineEvent> _klines = Channel.CreateBounded<KlineEvent>(
        new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = false });

    public ChannelReader<TickerEvent> Tickers => _tickers.Reader;
    public ChannelReader<KlineEvent> Klines => _klines.Reader;

    public ValueTask PublishAsync(MarketEvent evt, CancellationToken cancellationToken) => evt switch
    {
        TickerEvent t => _tickers.Writer.WriteAsync(t, cancellationToken),
        KlineEvent k => _klines.Writer.WriteAsync(k, cancellationToken),
        _ => ValueTask.CompletedTask
    };
}

public sealed class InMemorySignalEventBus : ISignalEventBus
{
    private readonly Channel<SignalEvent> _channel = Channel.CreateBounded<SignalEvent>(
        new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<SignalEvent> Signals => _channel.Reader;
    public ValueTask PublishAsync(SignalEvent evt, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(evt, cancellationToken);
}

public sealed class InMemoryBotEventBus : IBotEventBus
{
    private readonly Channel<BotEvent> _channel = Channel.CreateBounded<BotEvent>(
        new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.DropOldest });

    /// <summary>
    /// Fan-out hook for additional subscribers (e.g. Telegram notifier).
    /// The primary channel still feeds the SignalR broadcaster; this event fires in parallel.
    /// Handlers should be best-effort and never throw — exceptions are swallowed below.
    /// </summary>
    public event Func<BotEvent, CancellationToken, Task>? OnEvent;

    public ChannelReader<BotEvent> Events => _channel.Reader;

    public async ValueTask PublishAsync(BotEvent evt, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(evt, cancellationToken);
        var handlers = OnEvent;
        if (handlers is null) return;
        foreach (Func<BotEvent, CancellationToken, Task> h in handlers.GetInvocationList())
        {
            try { _ = h.Invoke(evt, cancellationToken); } catch { /* fan-out is best-effort */ }
        }
    }
}

public sealed class InMemoryTickStreamBus : ITickStreamBus
{
    private readonly Channel<BookTickerEvent> _book = Channel.CreateBounded<BookTickerEvent>(
        new BoundedChannelOptions(16384) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = false });
    private readonly Channel<AggTradeEvent> _trades = Channel.CreateBounded<AggTradeEvent>(
        new BoundedChannelOptions(16384) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = false });

    public ChannelReader<BookTickerEvent> BookTickers => _book.Reader;
    public ChannelReader<AggTradeEvent> AggTrades => _trades.Reader;
    public ValueTask PublishBookTickerAsync(BookTickerEvent evt, CancellationToken cancellationToken) => _book.Writer.WriteAsync(evt, cancellationToken);
    public ValueTask PublishAggTradeAsync(AggTradeEvent evt, CancellationToken cancellationToken) => _trades.Writer.WriteAsync(evt, cancellationToken);
}

public sealed class InMemoryVolumeSpikeBus : IVolumeSpikeBus
{
    private readonly Channel<VolumeSpikeEvent> _channel = Channel.CreateBounded<VolumeSpikeEvent>(
        new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<VolumeSpikeEvent> Spikes => _channel.Reader;
    public ValueTask PublishAsync(VolumeSpikeEvent evt, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(evt, cancellationToken);
}

public sealed class InMemoryOrderBookWallBus : IOrderBookWallBus
{
    private readonly Channel<OrderBookWallEvent> _channel = Channel.CreateBounded<OrderBookWallEvent>(
        new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<OrderBookWallEvent> Walls => _channel.Reader;
    public ValueTask PublishAsync(OrderBookWallEvent evt, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(evt, cancellationToken);
}
