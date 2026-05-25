using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantFlowBots.Application.Streaming;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

public sealed class BinanceTickStreamClient(
    IOptions<BinanceOptions> options,
    ITickStreamBus bus,
    ILogger<BinanceTickStreamClient> logger) : ITickStreamClient
{
    private readonly BinanceOptions _opt = options.Value;
    private readonly SemaphoreSlim _wsLock = new(1, 1);
    private readonly HashSet<string> _bookSubs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tradeSubs = new(StringComparer.OrdinalIgnoreCase);
    private ClientWebSocket? _ws;
    private int _msgId;

    public string ExchangeCode => "binance";

    public Task UpdateBookTickerSubscriptionsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        var target = symbols.Select(s => s.ToLowerInvariant() + "@bookTicker").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ApplyDiffAsync(_bookSubs, target, cancellationToken);
    }

    public Task UpdateAggTradeSubscriptionsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        var target = symbols.Select(s => s.ToLowerInvariant() + "@aggTrade").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ApplyDiffAsync(_tradeSubs, target, cancellationToken);
    }

    private async Task ApplyDiffAsync(HashSet<string> current, HashSet<string> target, CancellationToken cancellationToken)
    {
        var toAdd = target.Except(current).ToList();
        var toRemove = current.Except(target).ToList();
        current.Clear();
        foreach (var s in target) current.Add(s);

        if (_ws is { State: WebSocketState.Open })
        {
            if (toAdd.Count > 0) await SendAsync("SUBSCRIBE", toAdd, cancellationToken);
            if (toRemove.Count > 0) await SendAsync("UNSUBSCRIBE", toRemove, cancellationToken);
        }
    }

    private async Task SendAsync(string method, List<string> streams, CancellationToken cancellationToken)
    {
        if (_ws is null || _ws.State != WebSocketState.Open) return;
        await _wsLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var chunk in streams.Chunk(50))
            {
                var msg = JsonSerializer.Serialize(new { method, @params = chunk, id = Interlocked.Increment(ref _msgId) });
                var bytes = Encoding.UTF8.GetBytes(msg);
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
                await Task.Delay(50, cancellationToken);
            }
        }
        finally { _wsLock.Release(); }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                var url = $"{_opt.WebSocketBaseUrl}/ws";
                logger.LogInformation("BinanceTickStream connecting to {Url}", url);
                await ws.ConnectAsync(new Uri(url), cancellationToken);
                _ws = ws;
                logger.LogInformation("BinanceTickStream connected. Resubscribing {Book} bookTicker + {Trade} aggTrade.", _bookSubs.Count, _tradeSubs.Count);
                if (_bookSubs.Count > 0) await SendAsync("SUBSCRIBE", _bookSubs.ToList(), cancellationToken);
                if (_tradeSubs.Count > 0) await SendAsync("SUBSCRIBE", _tradeSubs.ToList(), cancellationToken);
                backoff = TimeSpan.FromSeconds(1);

                var buffer = new byte[16 * 1024];
                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    await HandleAsync(sb.ToString(), cancellationToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "BinanceTickStream error, reconnect in {S}s", backoff.TotalSeconds);
            }
            finally { _ws = null; }

            try { await Task.Delay(backoff, cancellationToken); } catch { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    private async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload)) return;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)) return;
            if (!root.TryGetProperty("e", out var eventType))
            {
                if (root.TryGetProperty("u", out _) && root.TryGetProperty("s", out _))
                {
                    await PublishBookTicker(root, cancellationToken);
                }
                return;
            }
            var t = eventType.GetString();
            if (t == "aggTrade") await PublishAggTrade(root, cancellationToken);
            else if (t == "bookTicker") await PublishBookTicker(root, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Tick stream parse error: {Payload}", payload.Length > 200 ? payload[..200] : payload);
        }
    }

    private ValueTask PublishBookTicker(JsonElement el, CancellationToken cancellationToken)
    {
        var symbol = el.GetProperty("s").GetString()!;
        var bid = Parse(el.GetProperty("b").GetString());
        var bidQty = Parse(el.GetProperty("B").GetString());
        var ask = Parse(el.GetProperty("a").GetString());
        var askQty = Parse(el.GetProperty("A").GetString());
        return bus.PublishBookTickerAsync(new BookTickerEvent("binance", symbol, bid, bidQty, ask, askQty, DateTimeOffset.UtcNow), cancellationToken);
    }

    private ValueTask PublishAggTrade(JsonElement el, CancellationToken cancellationToken)
    {
        var symbol = el.GetProperty("s").GetString()!;
        var price = Parse(el.GetProperty("p").GetString());
        var qty = Parse(el.GetProperty("q").GetString());
        var isBuyerMaker = el.GetProperty("m").GetBoolean();
        var tradeTime = DateTimeOffset.FromUnixTimeMilliseconds(el.GetProperty("T").GetInt64());
        return bus.PublishAggTradeAsync(new AggTradeEvent("binance", symbol, price, qty, isBuyerMaker, tradeTime, DateTimeOffset.UtcNow), cancellationToken);
    }

    private static decimal Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
