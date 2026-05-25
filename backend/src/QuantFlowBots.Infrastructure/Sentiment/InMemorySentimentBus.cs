using System.Threading.Channels;
using QuantFlowBots.Application.Sentiment;

namespace QuantFlowBots.Infrastructure.Sentiment;

public sealed class InMemorySentimentBus : ISentimentBus
{
    private readonly Channel<ScoredSentiment> _ch = Channel.CreateBounded<ScoredSentiment>(
        new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<ScoredSentiment> Events => _ch.Reader;

    public ValueTask PublishAsync(ScoredSentiment evt, CancellationToken cancellationToken)
        => _ch.Writer.WriteAsync(evt, cancellationToken);
}
