namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Cross-process circuit breaker for ALL outbound Binance REST traffic.
/// State + metadata lives in Redis so API and Worker share one source of truth — without that,
/// Worker keeps hammering Binance while API is already banned, which is exactly what we saw
/// extending the 418 ban repeatedly. Implementation: <see cref="RedisBinanceGate"/>.
/// </summary>
public interface IBinanceGate
{
    /// <summary>Throw <see cref="BinanceGateOpenException"/> immediately if the breaker is open.</summary>
    Task EnsureClosedAsync(string endpoint, CancellationToken cancellationToken);

    /// <summary>Read current state (for workers that want to yield/sleep instead of throw).</summary>
    Task<BinanceGateState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Record a successful response — keeps breaker closed.</summary>
    Task ReportSuccessAsync(string endpoint, CancellationToken cancellationToken);

    /// <summary>Record a failure; opens breaker with appropriate cooldown for 418/429.</summary>
    Task ReportFailureAsync(string endpoint, int statusCode, int? retryAfterSeconds, CancellationToken cancellationToken);
}

public sealed record BinanceGateState(
    bool IsOpen,
    DateTimeOffset? Until,
    int? StatusCode,
    string? Reason,
    string? LastEndpoint,
    int OpenCount24h);

public sealed class BinanceGateOpenException(BinanceGateState state)
    : Exception($"Binance gate is OPEN until {state.Until:O} (status={state.StatusCode}, reason={state.Reason})")
{
    public BinanceGateState State { get; } = state;
}
