using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The Atlas (features C18/B22): the whole catalogue as a 2D star field, read off the stored
/// projection (<c>artists.xy_x</c>/<c>xy_y</c>, produced by the offline PCA pass). Public — the map
/// is browsable signed out — but a signed-in caller with a taste vector also gets their projected
/// "you are here" position, placed on the very same plane (<see cref="AtlasProjector"/>).
///
/// <para>
/// Nothing is invented: only artists actually projected appear (an unprojected band is simply absent
/// from the field, not dropped at the origin), and the taste marker is omitted rather than faked
/// when the caller has no vector. The empty regions between the clusters are the gaps (B23).
/// </para>
/// </summary>
[ApiController]
[Route("api/atlas")]
[AllowAnonymous]
public class AtlasController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly AtlasProjector _projector;

    public AtlasController(GrimoireDbContext db, AtlasProjector projector)
    {
        _db = db;
        _projector = projector;
    }

    /// <summary>
    /// The star field, plus the caller's taste position when a valid bearer is attached and they
    /// have seeded a taste. Anonymous callers get the stars alone.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AtlasDto>> Get(CancellationToken ct)
    {
        List<AtlasStarDto> stars = await _db.Artists
            .AsNoTracking()
            .Where(a => a.XyX != null && a.XyY != null)
            .OrderBy(a => a.Name)
            .Select(a => new AtlasStarDto(a.Id, a.Name, a.Kind, a.Rank, a.XyX!.Value, a.XyY!.Value))
            .ToListAsync(ct);

        AtlasPointDto? taste = await ResolveTasteAsync(ct);

        return Ok(new AtlasDto(stars, taste));
    }

    /// <summary>
    /// Projects the signed-in caller's taste onto the Atlas plane, or null when they are anonymous,
    /// have no taste vector yet, or the projection basis cannot be built.
    /// </summary>
    private async Task<AtlasPointDto?> ResolveTasteAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        Guid? userId = CurrentUserId();
        if (userId is null)
        {
            return null;
        }

        float[]? taste = (await _db.UserTastes
                .AsNoTracking()
                .Where(t => t.UserId == userId.Value)
                .Select(t => t.Embedding)
                .FirstOrDefaultAsync(ct))
            ?.ToArray();

        if (taste is null)
        {
            return null;
        }

        (double X, double Y)? projected = await _projector.ProjectTasteAsync(taste, ct);

        return projected is null ? null : new AtlasPointDto(projected.Value.X, projected.Value.Y);
    }

    private Guid? CurrentUserId()
    {
        // MapInboundClaims is off, so the subject is the raw "sub" claim.
        string? sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out Guid id) ? id : null;
    }
}
