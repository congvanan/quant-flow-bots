using Microsoft.Extensions.Logging;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// DelegatingHandler that wraps every outbound Binance REST call.
///   - PRE: refuse the call if the shared gate is open (throws BinanceGateOpenException).
///   - PRE: increment per-endpoint call counter for observability.
///   - POST: on 418/429 → report failure (gate decides cooldown).
///
/// Attached to BinanceRestClient / BinanceFuturesRestClient / BinanceSpotSignedClient via
/// .AddHttpMessageHandler in DI — no per-method instrumentation needed.
/// </summary>
public sealed class BinanceGateHandler(
    IBinanceGate gate,
    BinanceCallCounter counter,
    ILogger<BinanceGateHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var endpoint = request.RequestUri?.AbsolutePath ?? "(unknown)";
        await gate.EnsureClosedAsync(endpoint, cancellationToken);
        counter.Increment(endpoint);

        var response = await base.SendAsync(request, cancellationToken);

        var sc = (int)response.StatusCode;
        if (sc is 418 or 429)
        {
            int? retryAfter = null;
            if (response.Headers.RetryAfter?.Delta is { } d) retryAfter = (int)d.TotalSeconds;
            else if (response.Headers.TryGetValues("Retry-After", out var v) && int.TryParse(v.FirstOrDefault(), out var s)) retryAfter = s;
            logger.LogWarning("Binance returned {Status} for {Endpoint} retryAfter={RetryAfter}", sc, endpoint, retryAfter);
            await gate.ReportFailureAsync(endpoint, sc, retryAfter, cancellationToken);
        }

        return response;
    }
}
