namespace QuantFlowBots.Infrastructure.Exchanges.Binance;

public sealed class BinanceOptions
{
    public string RestBaseUrl { get; set; } = "https://api.binance.com";
    public string WebSocketBaseUrl { get; set; } = "wss://stream.binance.com:9443";
    public string[] WatchSymbols { get; set; } = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT"];
}
