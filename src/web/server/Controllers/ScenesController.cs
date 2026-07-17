using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement V — Scenes (B20/C11). The metal underground organises itself by <b>scene</b>, not by
/// artist and not by country. A scene here is a (city, decade, sound family) cluster of bands, read
/// off the real <c>city</c>/<c>formed_year</c>/<c>tags</c> columns. This is explicitly NOT the Metal
/// Map country view (D17): the city and the decade carry as much weight as the sound.
///
/// <para>
/// Scenes are ranked by <b>lift</b>, not headcount. Ranking by headcount asked "which city has the
/// most bands wearing this tag?" and answered, forever, with the megacities wearing the emptiest
/// tags: Los Angeles / 2000s / "rock", London / 1960s / "psychedelic rock". Lift asks instead where
/// a sound is over-represented against the whole catalogue, which is the only version of the
/// question whose answers are scenes. See <see cref="SceneClusterer"/>.
/// </para>
///
/// <para>
/// Coverage is partial (city and formation year are sparse in the deep underground — R2), so a thin
/// catalogue yields few scenes and the front renders a designed empty state rather than a broken
/// grid. Nothing is invented: a band with no city, no year, or no tag naming a sound family simply
/// does not enter the clustering.
/// </para>
/// </summary>
[ApiController]
[Route("api/scenes")]
public class ScenesController : ControllerBase
{
    private const int DefaultMinSize = 6;
    private const int MaxScenes = 60;

    private readonly GrimoireDbContext _db;

    public ScenesController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The scenes in the catalogue, most concentrated first (B20/C11). <paramref name="minSize"/> is
    /// the floor on how many bands make a scene (default 6). The floor cannot go below 4, because
    /// lift and scarcity look identical from underneath: a city we know five bands in, all playing
    /// one sound, outscores every real scene on the map and is only a gap in the data. Empty when the
    /// catalogue holds no cluster that large: an honest "the margin is quiet here", not a fake grid.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SceneDto>>> List(
        [FromQuery] int minSize = DefaultMinSize,
        CancellationToken ct = default)
    {
        int floor = Math.Clamp(minSize, 4, 10);

        // Only bands with a city, a formation year and at least one tag can enter a scene; anything
        // less is not a datum we can cluster on. Kept to groups (a person is not a scene).
        var rows = await _db.Artists
            .AsNoTracking()
            .Where(a => a.Kind == ArtistKind.Group
                && a.City != null
                && a.FormedYear != null
                && a.Tags.Length > 0)
            .Select(a => new { a.Id, a.Name, a.Rank, a.City, a.FormedYear, a.Tags })
            .ToListAsync(ct);

        List<SceneClusterer.SceneInput> inputs = rows
            .Select(r => new SceneClusterer.SceneInput(
                r.Id,
                r.Name,
                r.Rank,
                r.City!,
                SceneClusterer.DecadeOf(r.FormedYear!.Value),
                r.Tags.Select(t => t.ToLowerInvariant()).ToList()))
            .ToList();

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(inputs, floor);

        List<SceneDto> dto = scenes
            .Take(MaxScenes)
            .Select(s => new SceneDto(
                s.City,
                s.Decade,
                s.Family,
                s.Size,
                Math.Round(s.Lift, 2),
                s.Bands.Select(b => new SceneBandDto(b.Id, b.Name, b.Rank)).ToList()))
            .ToList();

        return Ok(dto);
    }
}
