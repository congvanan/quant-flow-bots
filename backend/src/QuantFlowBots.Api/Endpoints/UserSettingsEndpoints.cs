using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Api.Endpoints;

public static class UserSettingsEndpoints
{
    public sealed record UserSettingsDto(
        bool TelegramAlertsEnabled,
        bool TelegramBotTokenConfigured,
        string? TelegramChatId,
        DateTimeOffset UpdatedAt);

    public sealed record UpdateUserSettingsRequest(
        bool? TelegramAlertsEnabled,
        string? TelegramBotToken,
        string? TelegramChatId);

    public static IEndpointRouteBuilder MapUserSettings(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/me/settings").WithTags("settings").RequireAuthorization();

        grp.MapGet("/", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var s = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return Results.Ok(ToDto(s));
        });

        grp.MapPut("/", async (UpdateUserSettingsRequest req, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var s = await db.UserSettings.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (s is null)
            {
                s = new UserSettings { UserId = userId };
                db.UserSettings.Add(s);
            }
            if (req.TelegramAlertsEnabled.HasValue) s.TelegramAlertsEnabled = req.TelegramAlertsEnabled.Value;
            if (req.TelegramBotToken is not null) s.TelegramBotToken = string.IsNullOrWhiteSpace(req.TelegramBotToken) ? null : req.TelegramBotToken.Trim();
            if (req.TelegramChatId is not null) s.TelegramChatId = string.IsNullOrWhiteSpace(req.TelegramChatId) ? null : req.TelegramChatId.Trim();
            s.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(s));
        });

        grp.MapPost("/telegram/test", async (QuantFlowBotsDbContext db, IHttpClientFactory httpFactory, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var s = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (s is null || string.IsNullOrWhiteSpace(s.TelegramBotToken) || string.IsNullOrWhiteSpace(s.TelegramChatId))
                return Results.BadRequest(new { error = "Telegram not configured" });
            try
            {
                using var client = httpFactory.CreateClient("telegram");
                client.Timeout = TimeSpan.FromSeconds(8);
                var payload = new
                {
                    chat_id = s.TelegramChatId,
                    text = "🤖 Quant Flow Bots — test message from settings",
                };
                using var resp = await client.PostAsJsonAsync(
                    $"https://api.telegram.org/bot{s.TelegramBotToken}/sendMessage", payload, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    return Results.BadRequest(new { error = $"Telegram {(int)resp.StatusCode}", body });
                return Results.Ok(new { sent = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }

    private static UserSettingsDto ToDto(UserSettings? s) => s is null
        ? new UserSettingsDto(false, false, null, DateTimeOffset.MinValue)
        : new UserSettingsDto(s.TelegramAlertsEnabled, !string.IsNullOrWhiteSpace(s.TelegramBotToken), s.TelegramChatId, s.UpdatedAt);

    private static Guid ParseUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
}
