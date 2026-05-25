using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Exchanges.Binance;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Security;
using QuantFlowBots.Infrastructure.Trading.LiveTrading;

namespace QuantFlowBots.Api.Endpoints;

public static class SettingsEndpoints
{
    public sealed record ExchangeDto(int Id, string Code, string Name, string RestBaseUrl, string WebSocketBaseUrl);
    public sealed record ApiKeyDto(
        Guid Id,
        int ExchangeId,
        string ExchangeCode,
        string Label,
        string KeyPreview,
        string Mode,
        bool IsActive,
        string PermissionsJson,
        DateTimeOffset? LastValidatedAt,
        DateTimeOffset? LastUsedAt,
        string? LastError,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record CreateApiKeyRequest(
        string ExchangeCode,
        string Label,
        string ApiKey,
        string ApiSecret,
        string? Mode,
        bool? IsActive,
        string? PermissionsJson);

    public sealed record UpdateApiKeyRequest(
        string? Label,
        string? ApiKey,
        string? ApiSecret,
        string? Mode,
        bool? IsActive,
        string? PermissionsJson);

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/settings").WithTags("settings").RequireAuthorization();

        grp.MapGet("/exchanges", async (QuantFlowBotsDbContext db, CancellationToken ct) =>
        {
            var exchanges = await db.Exchanges
                .OrderBy(e => e.Code)
                .Select(e => new ExchangeDto(e.Id, e.Code, e.Name, e.RestBaseUrl, e.WebSocketBaseUrl))
                .ToListAsync(ct);
            return Results.Ok(exchanges);
        });

        grp.MapGet("/api-keys", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var keys = await db.ApiKeys
                .Where(k => k.UserId == userId)
                .Include(k => k.Exchange)
                .OrderByDescending(k => k.UpdatedAt)
                .ToListAsync(ct);
            return Results.Ok(keys.Select(ToDto));
        });

        grp.MapPost("/api-keys", async (
            CreateApiKeyRequest req,
            QuantFlowBotsDbContext db,
            IApiKeyEncryption encryption,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var validation = ValidateSecretInput(req.Label, req.ApiKey, req.ApiSecret, req.PermissionsJson);
            if (validation is not null) return validation;

            if (!TryParseMode(req.Mode, out var mode))
                return Results.BadRequest(new { error = "unknown_mode" });

            var exchangeCode = req.ExchangeCode.Trim().ToLowerInvariant();
            var exchange = await db.Exchanges.FirstOrDefaultAsync(e => e.Code == exchangeCode, ct);
            if (exchange is null) return Results.BadRequest(new { error = "exchange_not_found" });

            var now = DateTimeOffset.UtcNow;
            var key = new ApiKey
            {
                UserId = ParseUserId(user),
                ExchangeId = exchange.Id,
                Label = req.Label.Trim(),
                KeyPreview = Preview(req.ApiKey),
                EncryptedKey = encryption.Encrypt(req.ApiKey.Trim()),
                EncryptedSecret = encryption.Encrypt(req.ApiSecret.Trim()),
                PermissionsJson = NormalizeJson(req.PermissionsJson),
                Mode = mode,
                IsActive = req.IsActive ?? true,
                LastValidatedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.ApiKeys.Add(key);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "duplicate_label_for_exchange" });
            }

            key.Exchange = exchange;
            return Results.Ok(ToDto(key));
        });

        grp.MapPut("/api-keys/{id:guid}", async (
            Guid id,
            UpdateApiKeyRequest req,
            QuantFlowBotsDbContext db,
            IApiKeyEncryption encryption,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var key = await db.ApiKeys.Include(k => k.Exchange).FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, ct);
            if (key is null) return Results.NotFound();

            if (req.Label is { } label)
            {
                if (string.IsNullOrWhiteSpace(label)) return Results.BadRequest(new { error = "label_required" });
                key.Label = label.Trim();
            }

            if (req.Mode is not null)
            {
                if (!TryParseMode(req.Mode, out var mode)) return Results.BadRequest(new { error = "unknown_mode" });
                key.Mode = mode;
            }

            if (req.PermissionsJson is not null)
            {
                if (!IsValidJson(req.PermissionsJson)) return Results.BadRequest(new { error = "permissions_json_invalid" });
                if (LiveTradingGate.HasWithdrawPermission(req.PermissionsJson))
                    return Results.BadRequest(new { error = "withdraw_permission_not_allowed" });
                key.PermissionsJson = NormalizeJson(req.PermissionsJson);
            }

            if (req.ApiKey is not null || req.ApiSecret is not null)
            {
                if (string.IsNullOrWhiteSpace(req.ApiKey) || string.IsNullOrWhiteSpace(req.ApiSecret))
                    return Results.BadRequest(new { error = "api_key_and_secret_required_together" });

                key.KeyPreview = Preview(req.ApiKey);
                key.EncryptedKey = encryption.Encrypt(req.ApiKey.Trim());
                key.EncryptedSecret = encryption.Encrypt(req.ApiSecret.Trim());
                key.LastValidatedAt = DateTimeOffset.UtcNow;
                key.LastError = null;
            }

            if (req.IsActive is { } active) key.IsActive = active;
            key.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "duplicate_label_for_exchange" });
            }

            return Results.Ok(ToDto(key));
        });

        grp.MapPost("/api-keys/{id:guid}/activate", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var key = await db.ApiKeys.Include(k => k.Exchange).FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, ct);
            if (key is null) return Results.NotFound();
            key.IsActive = true;
            key.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(key));
        });

        grp.MapPost("/api-keys/{id:guid}/deactivate", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var key = await db.ApiKeys.Include(k => k.Exchange).FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, ct);
            if (key is null) return Results.NotFound();
            key.IsActive = false;
            key.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(key));
        });

        grp.MapPost("/api-keys/{id:guid}/validate", async (
            Guid id,
            QuantFlowBotsDbContext db,
            IApiKeyEncryption encryption,
            BinanceFuturesRestClient futures,
            BinanceSpotSignedClient spot,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var key = await db.ApiKeys.Include(k => k.Exchange).FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, ct);
            if (key?.Exchange is null) return Results.NotFound();
            if (!LiveTradingGate.AllowedExchangeCodes.Contains(key.Exchange.Code))
                return Results.BadRequest(new { error = $"validate_not_supported_for_exchange:{key.Exchange.Code}" });
            try
            {
                bool canTrade, canWithdraw;
                object payload;
                if (key.Exchange.Code == "binance-spot-testnet")
                {
                    var account = await spot.GetAccountAsync(
                        new SpotCredential(encryption.Decrypt(key.EncryptedKey), encryption.Decrypt(key.EncryptedSecret), key.Exchange.RestBaseUrl),
                        ct);
                    canTrade = account.CanTrade;
                    canWithdraw = account.CanWithdraw;
                    payload = new
                    {
                        validatedAt = DateTimeOffset.UtcNow,
                        account.CanTrade,
                        account.CanWithdraw,
                        balances = account.Balances,
                    };
                }
                else
                {
                    var account = await futures.GetAccountAsync(
                        new FuturesCredential(encryption.Decrypt(key.EncryptedKey), encryption.Decrypt(key.EncryptedSecret), key.Exchange.RestBaseUrl),
                        ct);
                    canTrade = account.CanTrade;
                    canWithdraw = account.CanWithdraw;
                    payload = new
                    {
                        validatedAt = DateTimeOffset.UtcNow,
                        account.CanTrade,
                        account.CanWithdraw,
                        account.TotalWalletBalance,
                        account.AvailableBalance,
                    };
                }
                if (canWithdraw)
                {
                    key.LastError = "exchange_reports_withdraw_enabled";
                    key.IsActive = false;
                    await db.SaveChangesAsync(ct);
                    return Results.BadRequest(new { error = "exchange_reports_withdraw_enabled", hint = "Disable withdraw permission on the exchange and re-validate." });
                }
                key.LastValidatedAt = DateTimeOffset.UtcNow;
                key.LastUsedAt = DateTimeOffset.UtcNow;
                key.LastError = null;
                await db.SaveChangesAsync(ct);
                return Results.Ok(payload);
            }
            catch (Exception ex)
            {
                key.LastError = ex.Message.Length > 512 ? ex.Message[..512] : ex.Message;
                key.LastValidatedAt = null;
                await db.SaveChangesAsync(ct);
                return Results.BadRequest(new { error = "validate_failed", message = key.LastError });
            }
        });

        grp.MapDelete("/api-keys/{id:guid}", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, ct);
            if (key is null) return Results.NotFound();
            db.ApiKeys.Remove(key);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static ApiKeyDto ToDto(ApiKey key) => new(
        key.Id,
        key.ExchangeId,
        key.Exchange?.Code ?? string.Empty,
        key.Label,
        key.KeyPreview,
        key.Mode.ToString(),
        key.IsActive,
        key.PermissionsJson,
        key.LastValidatedAt,
        key.LastUsedAt,
        key.LastError,
        key.CreatedAt,
        key.UpdatedAt);

    private static IResult? ValidateSecretInput(string label, string apiKey, string apiSecret, string? permissionsJson)
    {
        if (string.IsNullOrWhiteSpace(label)) return Results.BadRequest(new { error = "label_required" });
        if (string.IsNullOrWhiteSpace(apiKey)) return Results.BadRequest(new { error = "api_key_required" });
        if (string.IsNullOrWhiteSpace(apiSecret)) return Results.BadRequest(new { error = "api_secret_required" });
        if (!IsValidJson(permissionsJson)) return Results.BadRequest(new { error = "permissions_json_invalid" });
        if (LiveTradingGate.HasWithdrawPermission(permissionsJson))
            return Results.BadRequest(new { error = "withdraw_permission_not_allowed", hint = "Remove withdraw/canWithdraw flag — the platform refuses keys with withdrawal rights." });
        return null;
    }

    private static bool TryParseMode(string? raw, out TradingMode mode)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            mode = TradingMode.Paper;
            return true;
        }

        return Enum.TryParse(raw, true, out mode);
    }

    private static bool IsValidJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        try
        {
            using var _ = JsonDocument.Parse(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        using var doc = JsonDocument.Parse(raw);
        return JsonSerializer.Serialize(doc.RootElement);
    }

    private static string Preview(string apiKey)
    {
        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 8) return "****";
        return $"{trimmed[..4]}...{trimmed[^4..]}";
    }

    private static Guid ParseUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
}
