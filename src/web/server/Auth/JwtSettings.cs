namespace Grimoire.Server.Auth;

/// <summary>
/// JWT bearer configuration, bound from the "Jwt" section of app configuration.
/// </summary>
public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Symmetric signing key for HS256. Must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 16;
}
