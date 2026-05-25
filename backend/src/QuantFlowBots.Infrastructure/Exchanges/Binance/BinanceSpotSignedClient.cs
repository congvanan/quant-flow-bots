using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Signed REST client for Binance Spot Testnet (https://testnet.binance.vision).
/// Mirrors <see cref="BinanceFuturesRestClient"/> but talks to /api/v3/* paths.
/// No leverage, no positionRisk — caller derives position state from balance + open orders.
/// </summary>
public sealed class BinanceSpotSignedClient(HttpClient http, ILogger<BinanceSpotSignedClient> logger)
{
    public const string TestnetBaseUrl = "https://testnet.binance.vision";

    public async Task<SpotAccountSnapshot> GetAccountAsync(SpotCredential cred, CancellationToken cancellationToken)
    {
        using var resp = await SendSignedAsync(HttpMethod.Get, cred, "/api/v3/account", new(), cancellationToken);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var balances = new List<SpotBalance>();
        foreach (var b in root.GetProperty("balances").EnumerateArray())
        {
            var free = ParseDec(b.GetProperty("free").GetString());
            var locked = ParseDec(b.GetProperty("locked").GetString());
            if (free == 0m && locked == 0m) continue;
            balances.Add(new SpotBalance(b.GetProperty("asset").GetString()!, free, locked));
        }
        bool canTrade = root.GetProperty("canTrade").GetBoolean();
        bool canWithdraw = root.GetProperty("canWithdraw").GetBoolean();
        bool canDeposit = root.GetProperty("canDeposit").GetBoolean();
        return new SpotAccountSnapshot(canTrade, canWithdraw, canDeposit, balances);
    }

    public async Task<SpotOrderResult> PlaceMarketOrderAsync(
        SpotCredential cred,
        string symbol,
        OrderSide side,
        decimal quantity,
        string clientOrderId,
        CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["side"] = side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = "MARKET",
            ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
            ["newClientOrderId"] = clientOrderId,
            ["newOrderRespType"] = "FULL",
        };
        using var resp = await SendSignedAsync(HttpMethod.Post, cred, "/api/v3/order", query, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Spot MARKET rejected status={Status} body={Body}", resp.StatusCode, body);
            return new SpotOrderResult(clientOrderId, null, OrderStatus.Rejected, 0m, quantity, 0m, 0m, body);
        }
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Average fill price from "fills"
        decimal avg = 0m, filledQty = 0m, totalComm = 0m;
        if (root.TryGetProperty("fills", out var fills))
        {
            foreach (var f in fills.EnumerateArray())
            {
                var p = ParseDec(f.GetProperty("price").GetString());
                var q = ParseDec(f.GetProperty("qty").GetString());
                avg += p * q;
                filledQty += q;
                if (f.TryGetProperty("commission", out var c))
                    totalComm += ParseDec(c.GetString());
            }
            if (filledQty > 0m) avg /= filledQty;
        }
        if (filledQty == 0m && root.TryGetProperty("executedQty", out var eq))
            filledQty = ParseDec(eq.GetString());
        if (avg == 0m && root.TryGetProperty("cummulativeQuoteQty", out var cqq) && filledQty > 0m)
            avg = ParseDec(cqq.GetString()) / filledQty;

        return new SpotOrderResult(
            clientOrderId,
            root.GetProperty("orderId").GetInt64().ToString(CultureInfo.InvariantCulture),
            MapStatus(root.GetProperty("status").GetString()),
            avg, ParseDec(root.GetProperty("origQty").GetString()), filledQty, totalComm, null);
    }

    public async Task<int> CancelOpenOrdersAsync(SpotCredential cred, string symbol, CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        using var resp = await SendSignedAsync(HttpMethod.Delete, cred, "/api/v3/openOrders", query, cancellationToken);
        if (!resp.IsSuccessStatusCode) return 0;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetArrayLength();
    }

    public async Task<int> CountOpenOrdersAsync(SpotCredential cred, string symbol, CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        using var resp = await SendSignedAsync(HttpMethod.Get, cred, "/api/v3/openOrders", query, cancellationToken);
        if (!resp.IsSuccessStatusCode) return 0;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetArrayLength();
    }

    private async Task<HttpResponseMessage> SendSignedAsync(
        HttpMethod method,
        SpotCredential cred,
        string path,
        SortedDictionary<string, string> query,
        CancellationToken ct)
    {
        query["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        query["recvWindow"] = "5000";
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var sig = Sign(cred.ApiSecret, qs);
        var fullPath = $"{path}?{qs}&signature={sig}";
        var url = new Uri(new Uri(string.IsNullOrWhiteSpace(cred.BaseUrl) ? TestnetBaseUrl : cred.BaseUrl), fullPath);
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-MBX-APIKEY", cred.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await http.SendAsync(req, ct);
    }

    private static string Sign(string secret, string query)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(query));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static decimal ParseDec(string? s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static OrderStatus MapStatus(string? raw) => raw switch
    {
        "NEW" => OrderStatus.New,
        "PARTIALLY_FILLED" => OrderStatus.PartiallyFilled,
        "FILLED" => OrderStatus.Filled,
        "CANCELED" => OrderStatus.Canceled,
        "REJECTED" => OrderStatus.Rejected,
        "EXPIRED" => OrderStatus.Expired,
        _ => OrderStatus.New,
    };
}

public sealed record SpotCredential(string ApiKey, string ApiSecret, string? BaseUrl = null);

public sealed record SpotBalance(string Asset, decimal Free, decimal Locked);

public sealed record SpotAccountSnapshot(bool CanTrade, bool CanWithdraw, bool CanDeposit, IReadOnlyList<SpotBalance> Balances);

public sealed record SpotOrderResult(
    string ClientOrderId,
    string? ExchangeOrderId,
    OrderStatus Status,
    decimal AveragePrice,
    decimal OriginalQuantity,
    decimal FilledQuantity,
    decimal Commission,
    string? RejectReason);
