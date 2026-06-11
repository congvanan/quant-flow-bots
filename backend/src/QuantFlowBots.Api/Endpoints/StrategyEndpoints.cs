using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuantFlowBots.Application.Strategies;
using QuantFlowBots.Domain.Entities;
using QuantFlowBots.Infrastructure.Persistence;
using QuantFlowBots.Infrastructure.Strategies;

namespace QuantFlowBots.Api.Endpoints;

public static class StrategyEndpoints
{
    public sealed record CreateStrategyRequest(string Name, string Kind, string? ParametersJson, string? Description);
    // Edit chỉ Name + ParametersJson + Description. KHÔNG cho đổi Kind vì params semantic gắn
    // với kind cụ thể — đổi kind = chiến lược khác hoàn toàn, nên clone thành strategy mới.
    public sealed record UpdateStrategyRequest(string? Name, string? ParametersJson, string? Description);
    // runningBotCount cho FE confirm modal: "N bot đang chạy strategy này, đổi params sẽ áp dụng
    // từ nến tiếp theo. Tiếp tục?". 0 → save thẳng không cần modal.
    public sealed record StrategyDto(Guid Id, string Name, string Kind, string ParametersJson, string? Description, DateTimeOffset CreatedAt, int RunningBotCount);

    public static IEndpointRouteBuilder MapStrategies(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/strategies").WithTags("strategies").RequireAuthorization();

        grp.MapGet("/kinds", (IStrategyFactory factory) => Results.Ok(factory.AvailableKinds));

        grp.MapGet("/", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            // LEFT JOIN bot counts trong 1 query — tránh N+1. State=Running enum value = 1.
            var list = await db.Strategies
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => new StrategyDto(
                    s.Id, s.Name, s.Kind, s.ParametersJson, s.Description, s.CreatedAt,
                    db.Bots.Count(b => b.StrategyId == s.Id && b.State == Domain.Enums.BotState.Running)))
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        grp.MapPost("/", async (CreateStrategyRequest req, QuantFlowBotsDbContext db, IStrategyFactory factory, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!factory.AvailableKinds.Contains(req.Kind))
                return Results.BadRequest(new { error = $"Unknown kind. Available: {string.Join(',', factory.AvailableKinds)}" });

            try
            {
                var probe = factory.Create(req.Kind);
                probe.Configure(StrategyBase.ParseJson(req.ParametersJson ?? "{}"));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var strategy = new Strategy
            {
                UserId = ParseUserId(user),
                Name = req.Name,
                Kind = req.Kind,
                ParametersJson = req.ParametersJson ?? "{}",
                Description = req.Description,
            };
            db.Strategies.Add(strategy);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new StrategyDto(strategy.Id, strategy.Name, strategy.Kind, strategy.ParametersJson, strategy.Description, strategy.CreatedAt, 0));
        });

        grp.MapPatch("/{id:guid}", async (Guid id, UpdateStrategyRequest req, QuantFlowBotsDbContext db, IStrategyFactory factory, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var s = await db.Strategies.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (s is null) return Results.NotFound();

            // Validate params nếu đổi — same logic như POST (probe Configure để bắt schema sai).
            if (req.ParametersJson is not null)
            {
                try
                {
                    var probe = factory.Create(s.Kind);
                    probe.Configure(StrategyBase.ParseJson(req.ParametersJson));
                }
                catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
                s.ParametersJson = req.ParametersJson;
            }
            if (!string.IsNullOrWhiteSpace(req.Name)) s.Name = req.Name.Trim();
            if (req.Description is not null) s.Description = req.Description;
            s.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var runningCount = await db.Bots.CountAsync(b => b.StrategyId == s.Id && b.State == Domain.Enums.BotState.Running, ct);
            return Results.Ok(new StrategyDto(s.Id, s.Name, s.Kind, s.ParametersJson, s.Description, s.CreatedAt, runningCount));
        });

        grp.MapDelete("/{id:guid}", async (Guid id, QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var s = await db.Strategies.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (s is null) return Results.NotFound();
            db.Strategies.Remove(s);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static Guid ParseUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
}
