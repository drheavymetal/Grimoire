using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The "see all" door (added 2026-07-15). Where the Rite serves one band blind, this returns the
/// NAMED bands behind a clicked tag or lyrical theme — explicit browse, not the blind tasting. It
/// exists so a tag or a theme surfaced elsewhere (the reveal, a band page, the Rite's theme lanes)
/// is a link the listener can open into the whole list, ordered most-heard first.
///
/// <para>
/// The pool is the discoverable catalogue narrowed to groups (a session drummer or a lone person is
/// not what a "bands with this tag" list wants), the same <see cref="DiscoverableArtists"/> gate the
/// Rite and search use. Nothing is invented: a tag or theme nobody wears yields an empty page, not a
/// fabricated grid.
/// </para>
/// </summary>
[ApiController]
[Route("api/browse")]
public class BrowseController : ControllerBase
{
    private const int DefaultTake = 48;
    private const int MaxTake = 100;

    private readonly GrimoireDbContext _db;

    public BrowseController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The bands wearing a tag (ILIKE substring, so a family catches its compounds), most-heard
    /// first. An empty or blank needle yields an empty page — never the whole catalogue.
    /// </summary>
    [HttpGet("tag/{needle}")]
    public async Task<ActionResult<BrowseResultDto>> ByTag(
        string needle,
        [FromQuery] int skip = 0,
        [FromQuery] int take = DefaultTake,
        CancellationToken ct = default)
    {
        string? clean = SearchNeedle.Clean(needle);

        if (clean is null)
        {
            return Ok(BrowseResultDto.Empty);
        }

        string pattern = $"%{clean}%";
        IQueryable<Artist> query = Base().Where(a => a.Tags.Any(t => EF.Functions.ILike(t, pattern)));

        return Ok(await PageAsync(query, skip, take, ct));
    }

    /// <summary>
    /// The bands behind a lyrical theme, most-heard first. <paramref name="kind"/> chooses the
    /// source: <c>lyrical</c> matches Metal Archives' curated <c>lyrical_themes</c> against the key;
    /// <c>mined</c> reads the key as a <see cref="TitleLexicon"/> theme id and keeps bands with a
    /// recording title evoking any of that theme's keywords (EXISTS over recordings, using the
    /// recordings-title trigram index). A blank key or an unknown mined theme yields an empty page.
    /// </summary>
    [HttpGet("theme/{key}")]
    public async Task<ActionResult<BrowseResultDto>> ByTheme(
        string key,
        [FromQuery] string kind = "lyrical",
        [FromQuery] int skip = 0,
        [FromQuery] int take = DefaultTake,
        CancellationToken ct = default)
    {
        string normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        string? clean = SearchNeedle.Clean(key);

        if (clean is null)
        {
            return Ok(BrowseResultDto.Empty);
        }

        IQueryable<Artist> query;

        if (normalizedKind == "mined")
        {
            IReadOnlyList<string> keywords = TitleLexicon.KeywordsFor(clean);

            if (keywords.Count == 0)
            {
                return Ok(BrowseResultDto.Empty);
            }

            string[] patterns = keywords.Select(k => $"%{k}%").ToArray();
            query = Base().Where(a => _db.Recordings.Any(r =>
                r.Release!.ArtistId == a.Id && patterns.Any(p => EF.Functions.ILike(r.Title, p))));
        }
        else if (normalizedKind == "lyrical")
        {
            string pattern = $"%{clean}%";
            query = Base().Where(a => a.LyricalThemes.Any(x => EF.Functions.ILike(x, pattern)));
        }
        else
        {
            return BadRequest(new { message = "kind must be 'lyrical' or 'mined'." });
        }

        return Ok(await PageAsync(query, skip, take, ct));
    }

    /// <summary>The browse pool: discoverable acts (embedding + discography, D23), narrowed to groups.</summary>
    private IQueryable<Artist> Base()
    {
        return _db.Artists.Discoverable().Where(a => a.Kind == ArtistKind.Group);
    }

    /// <summary>Counts the whole match, then returns one page of cards, most-heard first (nulls last), then by name.</summary>
    private static async Task<BrowseResultDto> PageAsync(
        IQueryable<Artist> query,
        int skip,
        int take,
        CancellationToken ct)
    {
        int offset = Math.Max(0, skip);
        int limit = Math.Clamp(take, 1, MaxTake);

        int total = await query.CountAsync(ct);

        List<BandCardDto> bands = await query
            .OrderBy(a => a.Listeners == null)
            .ThenByDescending(a => a.Listeners)
            .ThenBy(a => a.Name)
            .Skip(offset)
            .Take(limit)
            .Select(a => new BandCardDto(a.Id, a.Name, a.Rank, a.Country, a.Kind))
            .ToListAsync(ct);

        return new BrowseResultDto(total, bands);
    }
}
