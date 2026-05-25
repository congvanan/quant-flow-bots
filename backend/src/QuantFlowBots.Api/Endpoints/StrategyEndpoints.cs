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
    public sealed record StrategyDto(Guid Id, string Name, string Kind, string ParametersJson, string? Description, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapStrategies(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/strategies").WithTags("strategies").RequireAuthorization();

        grp.MapGet("/kinds", (IStrategyFactory factory) => Results.Ok(factory.AvailableKinds));

        grp.MapGet("/", async (QuantFlowBotsDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userId = ParseUserId(user);
            var list = await db.Strategies
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => new StrategyDto(s.Id, s.Name, s.Kind, s.ParametersJson, s.Description, s.CreatedAt))
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
            return Results.Ok(new StrategyDto(strategy.Id, strategy.Name, strategy.Kind, strategy.ParametersJson, strategy.Description, strategy.CreatedAt));
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
