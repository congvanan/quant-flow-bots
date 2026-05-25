using System.Text.Json;

namespace QuantFlowBots.Infrastructure.Trading;

public sealed record TpLevel(
    decimal ProfitPercent,
    decimal ClosePercent,
    decimal ClosePrice,
    decimal CloseQty,
    DateTimeOffset? HitAt);

public sealed record TpLevelTemplate(decimal ProfitPercent, decimal ClosePercent);

public static class TpLevelsCodec
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static List<TpLevelTemplate> ParseTemplate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<TpLevelTemplate>>(json, Opts) ?? new();
        }
        catch { return new(); }
    }

    public static List<TpLevel> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<TpLevel>>(json, Opts) ?? new();
        }
        catch { return new(); }
    }

    public static string Serialize(IEnumerable<TpLevel> levels)
        => JsonSerializer.Serialize(levels, Opts);

    public static List<TpLevel> BuildFromTemplate(
        IEnumerable<TpLevelTemplate> templates,
        decimal entryPrice,
        decimal originalQty)
    {
        var result = new List<TpLevel>();
        foreach (var t in templates)
        {
            if (t.ProfitPercent <= 0 || t.ClosePercent <= 0) continue;
            var price = entryPrice * (1m + t.ProfitPercent / 100m);
            var qty = originalQty * (t.ClosePercent / 100m);
            result.Add(new TpLevel(t.ProfitPercent, t.ClosePercent, price, qty, null));
        }
        return result;
    }
}
