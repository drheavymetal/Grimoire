using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Auth;
using Grimoire.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Registration, login, token refresh and session management. Passwords are handled by ASP.NET
/// Identity. Access tokens are short-lived stateless JWTs; refresh tokens are ROTATED and REVOCABLE
/// (D28): each is persisted as a SHA-256 hash in <c>refresh_tokens</c> and checked at refresh time.
///
/// <para>
/// The practical revocation window equals the access-token lifetime: revoking a refresh token stops
/// the next refresh, but an access token already minted stays valid until it expires (15 min), since
/// access tokens are not looked up per request by design (they are stateless).
/// </para>
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<GrimoireUser> _users;
    private readonly TokenService _tokens;
    private readonly GrimoireDbContext _db;

    public AuthController(UserManager<GrimoireUser> users, TokenService tokens, GrimoireDbContext db)
    {
        _users = users;
        _tokens = tokens;
        _db = db;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        GrimoireUser? existing = await _users.FindByEmailAsync(request.Email);

        if (existing is not null)
        {
            return Conflict(new { message = "An account with that email already exists." });
        }

        GrimoireUser user = new()
        {
            UserName = request.Email,
            Email = request.Email,
        };

        IdentityResult result = await _users.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        return Ok(await IssueAsync(user, ct));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        GrimoireUser? user = await _users.FindByEmailAsync(request.Email);

        if (user is null || !await _users.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(await IssueAsync(user, ct));
    }

    /// <summary>
    /// Rotates a refresh token (D28): the presented token is looked up by its hash and rejected if it
    /// is unknown, already revoked, or expired. A valid token is revoked and replaced with a fresh
    /// pair; presenting an ALREADY-revoked token is treated as reuse (a stolen/replayed token) and
    /// revokes every live session for that user, forcing a re-login everywhere.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        // Defence in depth: the token must still be a well-formed, unexpired refresh JWT we signed
        // before we even touch the database. A random string never gets a hash lookup.
        if (_tokens.ReadRefreshTokenUserId(request.RefreshToken) is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        string hash = TokenService.HashToken(request.RefreshToken);

        RefreshTokenRecord? record = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == hash, ct);

        if (record is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        if (record.RevokedAt is not null)
        {
            // A revoked token was presented again. Distinguish two cases so a benign race does not nuke
            // every device:
            //  - It has a SUCCESSOR (ReplacedByTokenHash set) → it was rotated away, so this is almost
            //    always a race: a second tab, or a reload mid-refresh, presenting its pre-rotation copy.
            //    Reject just this request; do NOT sweep the user's other sessions.
            //  - It has NO successor → it was revoked by an explicit logout. Replaying a logged-out
            //    token is suspicious (possible theft), so sweep every live session as the safe response.
            if (record.ReplacedByTokenHash is null)
            {
                await RevokeAllActiveAsync(record.UserId, ct);
                await _db.SaveChangesAsync(ct);

                return Unauthorized(new { message = "This session has been revoked. Sign in again." });
            }

            return Unauthorized(new { message = "This session was refreshed elsewhere. Sign in again." });
        }

        if (record.ExpiresAt <= DateTime.UtcNow)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        GrimoireUser? user = await _users.FindByIdAsync(record.UserId.ToString());

        if (user is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        TokenPair pair = _tokens.CreatePair(user);
        string newHash = TokenService.HashToken(pair.RefreshToken);

        // Rotate: mark the old row revoked, chain it to its successor, and persist the new session
        // carrying this request's device fingerprint.
        record.RevokedAt = DateTime.UtcNow;
        record.ReplacedByTokenHash = newHash;
        _db.RefreshTokens.Add(NewRecord(record.UserId, newHash, pair.RefreshTokenExpiresAt));

        await _db.SaveChangesAsync(ct);

        return Ok(ToResponse(pair));
    }

    /// <summary>Revokes the presented refresh token, ending that one session (D28). Idempotent 204.</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();
        string hash = TokenService.HashToken(request.RefreshToken);

        RefreshTokenRecord? record = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == hash && r.UserId == userId, ct);

        if (record is not null && record.RevokedAt is null)
        {
            record.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>Revokes ALL of the caller's live sessions (D28) — "sign out everywhere".</summary>
    [Authorize]
    [HttpPost("logout-all")]
    public async Task<ActionResult<LogoutAllResultDto>> LogoutAll(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        int revoked = await RevokeAllActiveAsync(userId, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new LogoutAllResultDto(revoked));
    }

    /// <summary>
    /// The caller's live sessions (non-revoked, non-expired), newest first. <see cref="SessionDto.Current"/>
    /// cannot be told from an access token alone, so it is always false here — documented best-effort.
    /// </summary>
    [Authorize]
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken ct)
    {
        Guid userId = CurrentUserId();
        DateTime now = DateTime.UtcNow;

        List<SessionDto> sessions = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new SessionDto(r.Id, r.CreatedAt, r.ExpiresAt, r.UserAgent, r.CreatedByIp, false))
            .ToListAsync(ct);

        return Ok(sessions);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Mints a token pair and persists its refresh session (register/login). Returns the response.</summary>
    private async Task<AuthResponse> IssueAsync(GrimoireUser user, CancellationToken ct)
    {
        TokenPair pair = _tokens.CreatePair(user);

        _db.RefreshTokens.Add(NewRecord(
            user.Id,
            TokenService.HashToken(pair.RefreshToken),
            pair.RefreshTokenExpiresAt));

        await _db.SaveChangesAsync(ct);

        return ToResponse(pair);
    }

    /// <summary>A fresh refresh-token row carrying this request's device fingerprint (best-effort).</summary>
    private RefreshTokenRecord NewRecord(Guid userId, string tokenHash, DateTime expiresAt)
    {
        return new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            UserAgent = Truncate(Request.Headers.UserAgent.ToString(), 512),
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };
    }

    /// <summary>Marks every live session for a user revoked, returning how many were swept. Does not save.</summary>
    private async Task<int> RevokeAllActiveAsync(Guid userId, CancellationToken ct)
    {
        List<RefreshTokenRecord> active = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        foreach (RefreshTokenRecord record in active)
        {
            record.RevokedAt = now;
        }

        return active.Count;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }

    private static AuthResponse ToResponse(TokenPair pair)
    {
        return new AuthResponse(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt);
    }

    private Guid CurrentUserId()
    {
        // MapInboundClaims is off, so the subject is the raw "sub" claim.
        string? sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(sub, out Guid id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated request carries no usable subject claim.");
    }
}
