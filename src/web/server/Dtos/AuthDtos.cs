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

/// <summary>Authentication response carrying a token pair.</summary>
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt);
