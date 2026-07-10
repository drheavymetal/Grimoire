using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Cover art proxy (feature B6). Resolves release-group covers from the Cover Art Archive
/// through an on-disk cache; the browser only ever talks to this endpoint, never to CAA.
/// </summary>
[ApiController]
[Route("api/covers")]
public class CoversController : ControllerBase
{
    private readonly CoverArtCache _covers;

    public CoversController(CoverArtCache covers)
    {
        _covers = covers;
    }

    /// <summary>Front cover for a release-group MBID. 404 when the archive has none.</summary>
    [HttpGet("release-group/{mbid:guid}")]
    public async Task<IActionResult> GetReleaseGroupFront(Guid mbid, CancellationToken ct = default)
    {
        CoverResult result = await _covers.GetAsync(mbid, ct);

        switch (result.Outcome)
        {
            case CoverOutcome.Found:
                // The cache is durable; let the browser hold the image for a week.
                Response.Headers.CacheControl = "public, max-age=604800";
                return PhysicalFile(result.FilePath!, "image/jpeg");

            case CoverOutcome.NotFound:
                // The miss is cached too, but let the browser back off for a day.
                Response.Headers.CacheControl = "public, max-age=86400";
                return NotFound();

            default:
                // Transient upstream failure — tell the client it is worth retrying later.
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
