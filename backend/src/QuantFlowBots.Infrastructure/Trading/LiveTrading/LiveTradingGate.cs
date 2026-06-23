using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Application.Trading;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Infrastructure.Trading.LiveTrading;

/// <summary>
/// Gate guarding any Live order placement. Must pass ALL conditions:
///   - Bot has ApiKeyId
///   - ApiKey IsActive
///   - ApiKey.Mode == Live (TradingMode.Live)
///   - ApiKey.LastValidatedAt within MaxValidationAgeDays
///   - ApiKey.PermissionsJson does NOT contain { withdraw: true } or { canWithdraw: true }
///   - ApiKey.Exchange.Code == "binance-futures-testnet" (phase F: testnet only)
/// </summary>
public sealed class LiveTradingGate(QuantFlowBotsDbContext db) : ILiveTradingGate
{
    public const int MaxValidationAgeDays = 7;
    public const string AllowedExchangeCode = "binance-futures-testnet"; // kept for back-compat
    public static readonly string[] AllowedExchangeCodes =
    [
        "binance-futures-testnet",
        "binance-spot-testnet",
    ];

    public async Task<LiveTradingGateResult> EvaluateAsync(Guid botId, CancellationToken cancellationToken)
    {
        var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == botId, cancellationToken);
        if (bot is null) return new(false, "bot_not_found");
        if (bot.ApiKeyId is null) return new(false, "no_api_key_linked");
        return await EvaluateKeyAsync(bot.ApiKeyId.Value, cancellationToken);
    }

    public async Task<LiveTradingGateResult> EvaluateKeyAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.Include(k => k.Exchange).AsNoTracking()
            .FirstOrDefaultAsync(k => k.Id == apiKeyId, cancellationToken);
        if (key is null) return new(false, "api_key_missing");
        if (!key.IsActive) return new(false, "api_key_inactive");
        if (key.Mode != Domain.Enums.TradingMode.Live) return new(false, "api_key_not_live_mode");
        if (key.Exchange is null || !AllowedExchangeCodes.Contains(key.Exchange.Code))
            return new(false, $"exchange_not_allowed:{key.Exchange?.Code}");
        if (key.LastValidatedAt is null) return new(false, "api_key_never_validated");
        if (DateTimeOffset.UtcNow - key.LastValidatedAt.Value > TimeSpan.FromDays(MaxValidationAgeDays))
            return new(false, "api_key_validation_stale");
        if (HasWithdrawPermission(key.PermissionsJson))
            return new(false, "api_key_has_withdraw_permission");
        return new(true, null);
    }

    public static bool HasWithdrawPermission(string? permissionsJson)
    {
        if (string.IsNullOrWhiteSpace(permissionsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(permissionsJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var name = prop.Name.ToLowerInvariant();
                if (name is "withdraw" or "canwithdraw" or "enablewithdrawals" && prop.Value.ValueKind == JsonValueKind.True)
                    return true;
            }
        }
        catch { /* invalid json is treated as no claim */ }
        return false;
    }
}
