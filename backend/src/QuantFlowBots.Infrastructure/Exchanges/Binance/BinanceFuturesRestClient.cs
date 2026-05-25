using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuantFlowBots.Domain.Enums;

namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

/// <summary>
/// Signed REST client for Binance Futures USDT-M. Defaults to testnet base url
/// (overridable per call). Caller owns credential lifecycle — no key material stored here.
/// </summary>
public sealed class BinanceFuturesRestClient(HttpClient http, ILogger<BinanceFuturesRestClient> logger)
{
    public const string TestnetBaseUrl = "https://testnet.binancefuture.com";

    public async Task<FuturesAccountSnapshot> GetAccountAsync(
        FuturesCredential cred, CancellationToken cancellationToken)
    {
        using var resp = await SignedGetAsync(cred, "/fapi/v2/account", new(), cancellationToken);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        return new FuturesAccountSnapshot(
            ParseDec(root.GetProperty("totalWalletBalance").GetString()),
            ParseDec(root.GetProperty("availableBalance").GetString()),
            root.GetProperty("canTrade").GetBoolean(),
            root.TryGetProperty("canWithdraw", out var cw) && cw.GetBoolean(),
            root.TryGetProperty("canDeposit", out var cd) && cd.GetBoolean());
    }

    public async Task<FuturesSymbolFilter[]> GetExchangeInfoAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var url = new Uri(new Uri(string.IsNullOrWhiteSpace(baseUrl) ? TestnetBaseUrl : baseUrl), "/fapi/v1/exchangeInfo");
        using var resp = await http.GetAsync(url, cancellationToken);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        var list = new List<FuturesSymbolFilter>();
        foreach (var s in doc.RootElement.GetProperty("symbols").EnumerateArray())
        {
            if (s.GetProperty("status").GetString() != "TRADING") continue;
            decimal minQty = 0m, stepSize = 0m, tickSize = 0m, minNotional = 0m;
            foreach (var f in s.GetProperty("filters").EnumerateArray())
            {
                switch (f.GetProperty("filterType").GetString())
                {
                    case "LOT_SIZE":
                        minQty = ParseDec(f.GetProperty("minQty").GetString());
                        stepSize = ParseDec(f.GetProperty("stepSize").GetString());
                        break;
                    case "PRICE_FILTER":
                        tickSize = ParseDec(f.GetProperty("tickSize").GetString());
                        break;
                    case "MIN_NOTIONAL":
                        if (f.TryGetProperty("notional", out var n)) minNotional = ParseDec(n.GetString());
                        break;
                }
            }
            list.Add(new FuturesSymbolFilter(
                s.GetProperty("symbol").GetString()!,
                minQty, stepSize, tickSize, minNotional,
                s.GetProperty("quantityPrecision").GetInt32(),
                s.GetProperty("pricePrecision").GetInt32()));
        }
        return list.ToArray();
    }

    public async Task<int> SetLeverageAsync(FuturesCredential cred, string symbol, int leverage, CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["leverage"] = leverage.ToString(CultureInfo.InvariantCulture),
        };
        using var resp = await SendSignedAsync(HttpMethod.Post, cred, "/fapi/v1/leverage", query, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("SetLeverage failed {Status} {Body}", resp.StatusCode, body);
            return 0;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("leverage").GetInt32();
    }

    public async Task<FuturesPosition?> GetPositionRiskAsync(FuturesCredential cred, string symbol, CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        using var resp = await SendSignedAsync(HttpMethod.Get, cred, "/fapi/v2/positionRisk", query, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            if (!string.Equals(p.GetProperty("symbol").GetString(), symbol, StringComparison.OrdinalIgnoreCase)) continue;
            var positionAmt = ParseDec(p.GetProperty("positionAmt").GetString());
            return new FuturesPosition(
                symbol.ToUpperInvariant(),
                positionAmt,
                ParseDec(p.GetProperty("entryPrice").GetString()),
                ParseDec(p.GetProperty("markPrice").GetString()),
                ParseDec(p.GetProperty("unRealizedProfit").GetString()),
                ParseDec(p.TryGetProperty("liquidationPrice", out var lp) ? lp.GetString() : "0"),
                p.TryGetProperty("leverage", out var lv) ? int.Parse(lv.GetString()!) : 1);
        }
        return new FuturesPosition(symbol.ToUpperInvariant(), 0m, 0m, 0m, 0m, 0m, 1);
    }

    public async Task<FuturesOrderResult> PlaceConditionalAsync(
        FuturesCredential cred,
        string symbol,
        OrderSide side,
        decimal quantity,
        decimal stopPrice,
        string clientOrderId,
        bool isTakeProfit,
        CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["side"] = side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = isTakeProfit ? "TAKE_PROFIT_MARKET" : "STOP_MARKET",
            ["stopPrice"] = stopPrice.ToString(CultureInfo.InvariantCulture),
            ["closePosition"] = "true",
            ["workingType"] = "MARK_PRICE",
            ["priceProtect"] = "true",
            ["newClientOrderId"] = clientOrderId,
        };
        using var resp = await SendSignedAsync(HttpMethod.Post, cred, "/fapi/v1/order", query, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Conditional order rejected {Status} {Body}", resp.StatusCode, body);
            return new FuturesOrderResult(clientOrderId, null, OrderStatus.Rejected, 0m, quantity, 0m, 0m, body);
        }
        using var doc = JsonDocument.Parse(body);
        return new FuturesOrderResult(
            clientOrderId,
            doc.RootElement.GetProperty("orderId").GetInt64().ToString(CultureInfo.InvariantCulture),
            OrderStatus.New, stopPrice, quantity, 0m, 0m, null);
    }

    public async Task CancelAllOpenOrdersAsync(FuturesCredential cred, string symbol, CancellationToken cancellationToken)
    {
        var query = new SortedDictionary<string, string> { ["symbol"] = symbol.ToUpperInvariant() };
        using var resp = await SendSignedAsync(HttpMethod.Delete, cred, "/fapi/v1/allOpenOrders", query, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("CancelAllOpenOrders failed {Status} {Body}", resp.StatusCode, body);
        }
    }

    public async Task<FuturesOrderResult> PlaceMarketOrderAsync(
        FuturesCredential cred,
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
            ["newOrderRespType"] = "RESULT",
        };
        using var resp = await SignedPostAsync(cred, "/fapi/v1/order", query, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Futures order rejected status={Status} body={Body}", resp.StatusCode, body);
            return new FuturesOrderResult(clientOrderId, null, OrderStatus.Rejected, 0m, quantity, 0m, 0m, body);
        }
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new FuturesOrderResult(
            clientOrderId,
            root.GetProperty("orderId").GetInt64().ToString(CultureInfo.InvariantCulture),
            MapStatus(root.GetProperty("status").GetString()),
            ParseDec(root.TryGetProperty("avgPrice", out var ap) ? ap.GetString() : "0"),
            ParseDec(root.TryGetProperty("origQty", out var oq) ? oq.GetString() : quantity.ToString(CultureInfo.InvariantCulture)),
            ParseDec(root.TryGetProperty("executedQty", out var eq) ? eq.GetString() : "0"),
            0m,
            null);
    }

    private Task<HttpResponseMessage> SignedGetAsync(
        FuturesCredential cred, string path, SortedDictionary<string, string> query, CancellationToken ct)
        => SendSignedAsync(HttpMethod.Get, cred, path, query, ct);

    private Task<HttpResponseMessage> SignedPostAsync(
        FuturesCredential cred, string path, SortedDictionary<string, string> query, CancellationToken ct)
        => SendSignedAsync(HttpMethod.Post, cred, path, query, ct);

    private async Task<HttpResponseMessage> SendSignedAsync(
        HttpMethod method,
        FuturesCredential cred,
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

public sealed record FuturesCredential(string ApiKey, string ApiSecret, string? BaseUrl = null);

public sealed record FuturesSymbolFilter(
    string Symbol,
    decimal MinQuantity,
    decimal StepSize,
    decimal TickSize,
    decimal MinNotional,
    int QuantityPrecision,
    int PricePrecision);

public sealed record FuturesPosition(
    string Symbol,
    decimal PositionAmt,
    decimal EntryPrice,
    decimal MarkPrice,
    decimal UnrealizedProfit,
    decimal LiquidationPrice,
    int Leverage);

public sealed record FuturesAccountSnapshot(
    decimal TotalWalletBalance,
    decimal AvailableBalance,
    bool CanTrade,
    bool CanWithdraw,
    bool CanDeposit);

public sealed record FuturesOrderResult(
    string ClientOrderId,
    string? ExchangeOrderId,
    OrderStatus Status,
    decimal AveragePrice,
    decimal OriginalQuantity,
    decimal FilledQuantity,
    decimal Commission,
    string? RejectReason);
