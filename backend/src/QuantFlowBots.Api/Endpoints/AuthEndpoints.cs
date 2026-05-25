using Microsoft.AspNetCore.Identity;
using QuantFlowBots.Api.Auth;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record RegisterRequest(string Email, string Password, string DisplayName);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record AuthResponse(string AccessToken, Guid UserId, string Email, string DisplayName);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/auth").WithTags("auth");

        grp.MapPost("/register", async (RegisterRequest req, UserManager<User> users, JwtTokenService jwt) =>
        {
            var user = new User { UserName = req.Email, Email = req.Email, DisplayName = req.DisplayName };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            return Results.Ok(new AuthResponse(jwt.CreateAccessToken(user), user.Id, user.Email!, user.DisplayName));
        });

        grp.MapPost("/login", async (LoginRequest req, UserManager<User> users, JwtTokenService jwt) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null || !await users.CheckPasswordAsync(user, req.Password))
                return Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
            return Results.Ok(new AuthResponse(jwt.CreateAccessToken(user), user.Id, user.Email!, user.DisplayName));
        });

        return app;
    }
}
