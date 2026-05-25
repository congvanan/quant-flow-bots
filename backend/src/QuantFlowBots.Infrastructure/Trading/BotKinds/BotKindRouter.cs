using Microsoft.Extensions.Logging;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Trading.BotKinds;

public sealed class BotKindRouter
{
    private readonly Dictionary<BotKind, IBotKindRuntime> _byKind;
    private readonly ILogger<BotKindRouter> _logger;

    public BotKindRouter(IEnumerable<IBotKindRuntime> runtimes, ILogger<BotKindRouter> logger)
    {
        _byKind = runtimes.ToDictionary(r => r.Kind);
        _logger = logger;
    }

    public Task DispatchAsync(BotKind kind, BotKindContext ctx, CancellationToken cancellationToken)
    {
        if (_byKind.TryGetValue(kind, out var rt)) return rt.EvaluateAsync(ctx, cancellationToken);
        _logger.LogWarning("No runtime registered for BotKind {Kind}", kind);
        return Task.CompletedTask;
    }
}
