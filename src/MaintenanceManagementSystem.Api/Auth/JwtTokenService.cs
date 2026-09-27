using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MaintenanceManagementSystem.Api.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MaintenanceManagementSystem.Api.Auth;

/// <summary>
/// Creates signed JWT access tokens.
/// </summary>
public class JwtTokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(IOptions<JwtSettings> settingsOptions)
    {
        _settings = settingsOptions.Value;
    }

    /// <summary>
    /// Builds a token for the user. The organization id and role always come
    /// from the server-side user record — never from client input.
    /// </summary>
    public (string AccessToken, DateTime ExpiresAtUtc) CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim(AppClaimTypes.UserId, user.Id.ToString()),
            new Claim(AppClaimTypes.OrganizationId, user.OrganizationId.ToString()),
            new Claim(AppClaimTypes.Role, user.Role.ToString()),
            new Claim(AppClaimTypes.Email, user.Email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_settings.LifetimeMinutes);
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
