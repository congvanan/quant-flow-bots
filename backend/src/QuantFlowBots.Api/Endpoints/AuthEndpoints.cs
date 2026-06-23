using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using QuantFlowBots.Api.Auth;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record RegisterRequest(string Email, string Password, string DisplayName);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
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

        grp.MapPost("/forgot-password", async (ForgotPasswordRequest req, UserManager<User> users, IWebHostEnvironment env, IDataProtectionProvider protection) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null)
                return Results.Ok(new { message = "If that email exists, reset instructions have been prepared." });

            var protector = protection.CreateProtector("QuantFlowBots.Auth.PasswordReset.v1");
            var stamp = await users.GetSecurityStampAsync(user);
            var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
            var token = protector.Protect($"{user.Id:N}|{stamp}|{expires}");
            if (env.IsDevelopment())
                return Results.Ok(new { message = "Development reset token generated.", resetToken = token });

            return Results.Ok(new { message = "If that email exists, reset instructions have been sent." });
        });

        grp.MapPost("/reset-password", async (ResetPasswordRequest req, UserManager<User> users, IDataProtectionProvider protection) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null)
                return Results.BadRequest(new { error = "Invalid reset request." });

            var protector = protection.CreateProtector("QuantFlowBots.Auth.PasswordReset.v1");
            string raw;
            try { raw = protector.Unprotect(req.Token); }
            catch { return Results.BadRequest(new { error = "Invalid or expired reset token." }); }

            var parts = raw.Split('|');
            if (parts.Length != 3
                || !Guid.TryParseExact(parts[0], "N", out var tokenUserId)
                || tokenUserId != user.Id
                || !long.TryParse(parts[2], out var expires)
                || DateTimeOffset.FromUnixTimeSeconds(expires) < DateTimeOffset.UtcNow)
            {
                return Results.BadRequest(new { error = "Invalid or expired reset token." });
            }

            var currentStamp = await users.GetSecurityStampAsync(user);
            if (!string.Equals(parts[1], currentStamp, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Invalid or expired reset token." });

            if (await users.HasPasswordAsync(user))
            {
                var remove = await users.RemovePasswordAsync(user);
                if (!remove.Succeeded)
                    return Results.BadRequest(new { errors = remove.Errors.Select(e => e.Description) });
            }

            var add = await users.AddPasswordAsync(user, req.NewPassword);
            if (!add.Succeeded)
                return Results.BadRequest(new { errors = add.Errors.Select(e => e.Description) });

            await users.UpdateSecurityStampAsync(user);

            return Results.Ok(new { message = "Password has been reset." });
        });

        return app;
    }
}
