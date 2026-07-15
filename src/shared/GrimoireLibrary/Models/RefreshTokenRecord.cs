namespace Grimoire.Library.Models;

/// <summary>
/// A persisted, revocable refresh token (D28: refresh tokens become revocable). The raw token is a
/// JWT held only by the client; here we store its SHA-256 hash — never the token itself — so a leak
/// of this table cannot mint sessions. A row is written on register/login and ROTATED on every
/// refresh: the old row is marked <see cref="RevokedAt"/> and points at its successor through
/// <see cref="ReplacedByTokenHash"/>, and presenting an already-revoked token is treated as a reuse
/// attack. Access tokens stay stateless short-lived JWTs; enforcement happens at refresh time, so the
/// practical revocation window equals the access-token lifetime.
/// </summary>
public class RefreshTokenRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the refresh token. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>When this token was revoked (logout, rotation, or reuse response), or null while live.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>On rotation, the hash of the token that superseded this one — the audit trail of the chain.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>The client's User-Agent when the session was created, for the sessions list. Best-effort.</summary>
    public string? UserAgent { get; set; }

    /// <summary>The client's IP when the session was created, for the sessions list. Best-effort.</summary>
    public string? CreatedByIp { get; set; }
}
