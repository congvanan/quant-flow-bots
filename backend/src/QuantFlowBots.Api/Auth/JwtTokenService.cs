using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using QuantFlowBots.Domain.Entities;

namespace QuantFlowBots.Api.Auth;

public sealed class JwtTokenService(IConfiguration config)
{
    public string CreateAccessToken(User user)
    {
        var section = config.GetSection("Jwt");
        var signingKey = section["SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey missing.");
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("display_name", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: section["Issuer"],
            audience: section["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(section["AccessTokenMinutes"] ?? "60")),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
