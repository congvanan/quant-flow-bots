using System.Text.Json;
using QuantFlowBots.Application.Strategies;

namespace QuantFlowBots.Infrastructure.Strategies;

public abstract class StrategyBase : IStrategy
{
    public abstract string Kind { get; }
    public abstract int WarmupBars { get; }
    public abstract void Configure(IReadOnlyDictionary<string, object?> parameters);
    public abstract StrategyDecision? OnCandle(QuantFlowBots.Application.Exchanges.CandleData candle, IStrategyContext context);

    protected static int Int(IReadOnlyDictionary<string, object?> p, string key, int defaultValue)
        => p.TryGetValue(key, out var v) && v is not null && int.TryParse(v.ToString(), out var i) ? i : defaultValue;

    protected static decimal Dec(IReadOnlyDictionary<string, object?> p, string key, decimal defaultValue)
        => p.TryGetValue(key, out var v) && v is not null && decimal.TryParse(v.ToString(), out var d) ? d : defaultValue;

    public static IReadOnlyDictionary<string, object?> ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.GetDecimal(),
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => prop.Value.ToString(),
            };
        }
        return dict;
    }
}
