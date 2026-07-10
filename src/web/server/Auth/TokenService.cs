using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Grimoire.Library.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Grimoire.Server.Auth;

/// <summary>
/// Issues and validates JWTs. Access tokens are short-lived (15 min); refresh tokens
/// are longer-lived (16 days) and carry a distinct "token_type" claim so an access
/// token can never be replayed at the refresh endpoint.
/// </summary>
public class TokenService
{
    private const string TokenTypeClaim = "token_type";
    private const string AccessType = "access";
    private const string RefreshType = "refresh";

    private readonly JwtSettings _settings;

    public TokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public TokenPair CreatePair(GrimoireUser user)
    {
        DateTime now = DateTime.UtcNow;
        DateTime accessExpiry = now.AddMinutes(_settings.AccessTokenMinutes);
        DateTime refreshExpiry = now.AddDays(_settings.RefreshTokenDays);

        string access = WriteToken(user, AccessType, accessExpiry);
        string refresh = WriteToken(user, RefreshType, refreshExpiry);

        return new TokenPair(access, refresh, accessExpiry);
    }

    /// <summary>
    /// Validates a refresh token and returns the user id it was issued for, or null
    /// if the token is invalid, expired, or not a refresh token.
    /// </summary>
    public Guid? ReadRefreshTokenUserId(string refreshToken)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_settings.SigningKey));
        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        JwtSecurityTokenHandler handler = new()
        {
            // Keep short claim names ("sub", "token_type") instead of remapping them to
            // legacy XML URIs, so the reads below find them.
            MapInboundClaims = false,
        };

        try
        {
            ClaimsPrincipal principal = handler.ValidateToken(refreshToken, parameters, out _);

            if (principal.FindFirstValue(TokenTypeClaim) != RefreshType)
            {
                return null;
            }

            string? subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (Guid.TryParse(subject, out Guid userId))
            {
                return userId;
            }

            return null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    private string WriteToken(GrimoireUser user, string tokenType, DateTime expiry)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_settings.SigningKey));
        SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(TokenTypeClaim, tokenType),
        ];

        JwtSecurityToken token = new(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiry,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>A freshly issued access/refresh token pair.</summary>
public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt);
