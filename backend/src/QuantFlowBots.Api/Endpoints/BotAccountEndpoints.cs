using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Domain.Enums;
using QuantFlowBots.Infrastructure.Persistence;

namespace QuantFlowBots.Api.Endpoints;

/// <summary>
/// Quản lý các account (BotAccount) tham gia 1 bot — multi-account fan-out
/// (xem [[project-quantflow-multi-account]]). Mỗi account: 1 api key + vốn riêng + weight riêng
/// + kill-switch riêng. Bot không có account nào → chạy single-account legacy qua Bot.ApiKeyId.
/// </summary>
public static class BotAccountEndpoints
{
    public sealed record CreateBotAccountRequest(Guid ApiKeyId, string? Label, decimal? Weight, decimal? BaseEquityUsdt, bool? IsActive);
    public sealed record UpdateBotAccountRequest(string? Label, decimal? Weight, decimal? BaseEquityUsdt, bool? IsActive);
    public sealed record BotAccountDto(
        Guid Id, Guid BotId, Guid ApiKeyId, string ExchangeCode, string KeyLabel, string Label,
        decimal Weight, decimal BaseEquityUsdt, bool IsActive,
        DateTimeOffset? KillSwitchTrippedAt, string? KillSwitchReason,
        // Stats per account (tính từ positions tag ApiKeyId)
        int OpenPositions, decimal RealizedPnl, decimal PnlToday, int TotalTrades, decimal WinRatePercent);

    public static IEndpointRouteBuilder MapBotAccounts(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/bots/{botId:guid}/accounts").WithTags("bot-accounts").RequireAuthorization();

        grp.MapGet("/", async (Guid botId, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == botId && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();
            var dtos = await BuildDtosAsync(db, botId, ct);
            return Results.Ok(dtos);
        });

        grp.MapPost("/", async (Guid botId, CreateBotAccountRequest req, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == botId && b.UserId == userId, ct);
            if (bot is null) return Results.NotFound();

            var key = await db.ApiKeys.Include(k => k.Exchange).FirstOrDefaultAsync(k => k.Id == req.ApiKeyId && k.UserId == userId, ct);
            if (key is null) return Results.BadRequest(new { error = "api_key_not_found" });

            var mismatch = MarketMismatch(key.Exchange?.Code, bot.ExecutionMarket);
            if (mismatch is not null) return Results.BadRequest(new { error = mismatch });

            if (await db.BotAccounts.AnyAsync(a => a.BotId == botId && a.ApiKeyId == req.ApiKeyId, ct))
                return Results.BadRequest(new { error = "account_already_linked" });

            var weight = req.Weight ?? 1m;
            if (weight <= 0m) return Results.BadRequest(new { error = "weight_must_be_positive" });

            var acct = new BotAccount
            {
                BotId = botId,
                ApiKeyId = req.ApiKeyId,
                Label = string.IsNullOrWhiteSpace(req.Label) ? key.Label : req.Label!.Trim(),
                Weight = weight,
                BaseEquityUsdt = req.BaseEquityUsdt ?? bot.BaseEquityUsdt,
                IsActive = req.IsActive ?? true,
            };
            db.BotAccounts.Add(acct);
            await db.SaveChangesAsync(ct);
            var dtos = await BuildDtosAsync(db, botId, ct);
            return Results.Ok(dtos.First(d => d.Id == acct.Id));
        });

        grp.MapPatch("/{accountId:guid}", async (Guid botId, Guid accountId, UpdateBotAccountRequest req, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var acct = await db.BotAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.BotId == botId, ct);
            if (acct is null) return Results.NotFound();
            var owned = await db.Bots.AnyAsync(b => b.Id == botId && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();

            if (req.Label is not null) acct.Label = req.Label.Trim();
            if (req.Weight.HasValue)
            {
                if (req.Weight.Value <= 0m) return Results.BadRequest(new { error = "weight_must_be_positive" });
                acct.Weight = req.Weight.Value;
            }
            if (req.BaseEquityUsdt.HasValue) acct.BaseEquityUsdt = req.BaseEquityUsdt.Value;
            if (req.IsActive.HasValue) acct.IsActive = req.IsActive.Value;
            acct.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            var dtos = await BuildDtosAsync(db, botId, ct);
            return Results.Ok(dtos.First(d => d.Id == acct.Id));
        });

        grp.MapPost("/{accountId:guid}/kill-switch/reset", async (Guid botId, Guid accountId, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var owned = await db.Bots.AnyAsync(b => b.Id == botId && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();
            var acct = await db.BotAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.BotId == botId, ct);
            if (acct is null) return Results.NotFound();
            acct.KillSwitchTrippedAt = null;
            acct.KillSwitchReason = null;
            acct.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { tripped = false });
        });

        grp.MapDelete("/{accountId:guid}", async (Guid botId, Guid accountId, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var owned = await db.Bots.AnyAsync(b => b.Id == botId && b.UserId == userId, ct);
            if (!owned) return Results.NotFound();
            var acct = await db.BotAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.BotId == botId, ct);
            if (acct is null) return Results.NotFound();
            db.BotAccounts.Remove(acct);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<List<BotAccountDto>> BuildDtosAsync(QuantFlowBotsDbContext db, Guid botId, CancellationToken ct)
    {
        var accounts = await db.BotAccounts
            .Where(a => a.BotId == botId)
            .Include(a => a.ApiKey).ThenInclude(k => k!.Exchange)
            .OrderByDescending(a => a.Weight)
            .ToListAsync(ct);
        if (accounts.Count == 0) return new List<BotAccountDto>();

        var keyIds = accounts.Select(a => a.ApiKeyId).ToList();
        var positions = await db.Positions
            .Where(p => p.BotId == botId && p.ApiKeyId != null && keyIds.Contains(p.ApiKeyId!.Value))
            .Select(p => new { p.ApiKeyId, p.Status, p.RealizedPnl, p.ClosedAt })
            .ToListAsync(ct);
        var dayStart = DateTimeOffset.UtcNow.AddHours(-24);

        return accounts.Select(a =>
        {
            var mine = positions.Where(p => p.ApiKeyId == a.ApiKeyId).ToList();
            var closed = mine.Where(p => p.Status == PositionStatus.Closed).ToList();
            var wins = closed.Count(p => p.RealizedPnl > 0);
            return new BotAccountDto(
                a.Id, a.BotId, a.ApiKeyId, a.ApiKey?.Exchange?.Code ?? "", a.ApiKey?.Label ?? "", a.Label,
                a.Weight, a.BaseEquityUsdt, a.IsActive, a.KillSwitchTrippedAt, a.KillSwitchReason,
                mine.Count(p => p.Status == PositionStatus.Open),
                closed.Sum(p => p.RealizedPnl),
                closed.Where(p => p.ClosedAt >= dayStart).Sum(p => p.RealizedPnl),
                closed.Count,
                closed.Count > 0 ? (decimal)wins / closed.Count * 100m : 0m);
        }).ToList();
    }

    // Cùng quy ước với BotEndpoints.ValidateApiKeyMarketAsync: futures key ⇄ Futures, spot ⇄ Spot.
    // Exchange code khác (seed/dev) → bỏ qua (không gắt).
    private static string? MarketMismatch(string? exchangeCode, MarketKind market) => exchangeCode switch
    {
        "binance-futures-testnet" when market != MarketKind.Futures
            => "api_key_market_mismatch: futures key nhưng bot ExecutionMarket=Spot",
        "binance-spot-testnet" when market != MarketKind.Spot
            => "api_key_market_mismatch: spot key nhưng bot ExecutionMarket=Futures",
        _ => null,
    };

    private static Guid ParseUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
}
