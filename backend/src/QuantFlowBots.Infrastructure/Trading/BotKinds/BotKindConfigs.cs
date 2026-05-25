using System.Text.Json;

namespace QuantFlowBots.Infrastructure.Trading.BotKinds;

public sealed record DcaConfig(
    decimal BaseQuoteAmount = 100m,
    decimal SafetyQuoteAmount = 100m,
    int MaxSafetyOrders = 5,
    decimal PriceStepPercent = 1.5m,
    decimal VolumeScale = 1.5m,
    decimal TakeProfitPercent = 1.0m);

public sealed record GridConfig(
    decimal UpperPrice = 0m,
    decimal LowerPrice = 0m,
    int GridLevels = 10,
    decimal QuotePerGrid = 50m,
    decimal TakeProfitPercent = 0.5m);

public sealed record ScalpConfig(
    decimal QuoteAmount = 50m,
    decimal SpreadBpsMin = 1m,
    decimal SpreadBpsMax = 30m,
    decimal TakeProfitPercent = 0.2m,
    decimal StopLossPercent = 0.3m,
    int CooldownSeconds = 30);

public static class BotKindConfigCodec
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static DcaConfig ParseDca(string? json) =>
        TryParse<DcaConfig>(json) ?? new DcaConfig();

    public static GridConfig ParseGrid(string? json) =>
        TryParse<GridConfig>(json) ?? new GridConfig();

    public static ScalpConfig ParseScalp(string? json) =>
        TryParse<ScalpConfig>(json) ?? new ScalpConfig();

    public static string Serialize<T>(T cfg) where T : class =>
        JsonSerializer.Serialize(cfg, Opts);

    private static T? TryParse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Opts); }
        catch { return null; }
    }
}
