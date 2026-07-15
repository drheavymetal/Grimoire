using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

// Validation attributes sit on the record's constructor parameters (not the generated
// properties): MVC validates positional-record parameters, and metadata on the
// property throws at model-binding time.

/// <summary>Registration request.</summary>
public record RegisterRequest(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password);

/// <summary>Login request.</summary>
public record LoginRequest(
    [Required][EmailAddress] string Email,
    [Required] string Password);

/// <summary>Token refresh request.</summary>
public record RefreshRequest(
    [Required] string RefreshToken);

/// <summary>Logout request: the refresh token whose session to revoke (D28).</summary>
public record LogoutRequest(
    [Required] string RefreshToken);

/// <summary>Authentication response carrying a token pair.</summary>
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt);

/// <summary>
/// One active refresh-token session in the caller's list (D28). <see cref="Current"/> marks the
/// session the caller is refreshing with when it can be told apart; from an access token alone it
/// cannot, so it is best-effort and may be false for all.
/// </summary>
public record SessionDto(
    Guid Id,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string? UserAgent,
    string? CreatedByIp,
    bool Current);

/// <summary>The outcome of revoking every session: how many were still live before the sweep.</summary>
public record LogoutAllResultDto(int Revoked);
