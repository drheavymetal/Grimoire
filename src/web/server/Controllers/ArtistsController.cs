using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Artist search and detail (features B1, B4, B5). Search uses PostgreSQL trigram
/// similarity (pg_trgm) against a GIN index; nothing here is faked.
/// </summary>
[ApiController]
[Route("api/artists")]
public class ArtistsController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly ArtistDetailBuilder _details;

    public ArtistsController(GrimoireDbContext db, ArtistDetailBuilder details)
    {
        _db = db;
        _details = details;
    }

    /// <summary>
    /// Fuzzy artist search by name, ordered by trigram similarity. Uses the `%`
    /// operator (GIN trigram index) to filter and `similarity()` to rank.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArtistSummaryDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<ArtistSummaryDto>());
        }

        int take = Math.Clamp(limit, 1, 100);
        string term = q.Trim();

        List<ArtistSummaryDto> results = await _db.Artists
            .Where(a => EF.Functions.TrigramsAreSimilar(a.Name, term))
            .OrderByDescending(a => EF.Functions.TrigramsSimilarity(a.Name, term))
            .ThenBy(a => a.Name)
            .Take(take)
            .Select(a => new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank))
            .ToListAsync(ct);

        return Ok(results);
    }

    /// <summary>Full artist detail: identity, tags, releases and bloodline edges.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArtistDetailDto>> GetById(Guid id, CancellationToken ct = default)
    {
        ArtistDetailDto? dto = await _details.BuildAsync(id, ct);

        if (dto is null)
        {
            return NotFound();
        }

        return Ok(dto);
    }

    /// <summary>
    /// Per-release credits for a band's discography (feature B9): who performed on each release
    /// (official member vs guest, with their instruments) and who produced, engineered, mixed or
    /// mastered it. Keyed by release id so the front matches it to the discography it already holds.
    /// Reads the real <c>credits</c> rows; releases the ETL never reached simply do not appear, and
    /// the front renders a designed "no credits" state for them (R2 — the ficha degrades with dignity).
    /// </summary>
    [HttpGet("{id:guid}/credits")]
    public async Task<ActionResult<IReadOnlyList<ReleaseCreditsDto>>> Credits(Guid id, CancellationToken ct = default)
    {
        bool exists = await _db.Artists.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        // Credits sit on releases; a release belongs to this band, and each credit's artist is the
        // performer (a member or a guest). Join to the performer for their name and rank.
        var rows = await _db.Credits
            .AsNoTracking()
            .Where(c => c.ReleaseId != null
                && _db.Releases.Any(r => r.Id == c.ReleaseId && r.ArtistId == id))
            .Join(
                _db.Artists.AsNoTracking(),
                c => c.ArtistId,
                a => a.Id,
                (c, a) => new { c.ReleaseId, c.ArtistId, a.Name, a.Rank, c.Role, c.Instrument, c.IsGuest })
            .ToListAsync(ct);

        IReadOnlyList<ReleaseCreditsDto> grouped = CreditGrouping.Group(
            rows.Select(r => new CreditGrouping.CreditRow(
                r.ReleaseId!.Value, r.ArtistId, r.Name, r.Rank, r.Role, r.Instrument, r.IsGuest)));

        return Ok(grouped);
    }

    /// <summary>
    /// "The disc where everything changed" (feature B12): the release with the greatest lineup
    /// turnover around its date, and who joined and left near it. Reuses the interval logic of the
    /// Gantt (<see cref="LineupTurnover"/> over <c>LineupIntervalResolver</c>). Returns 204 No Content
    /// when no dated release sees any change — an honest empty state, never invented drama.
    /// </summary>
    [HttpGet("{id:guid}/pivotal-release")]
    public async Task<ActionResult<PivotalReleaseDto>> PivotalRelease(Guid id, CancellationToken ct = default)
    {
        bool exists = await _db.Artists.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        List<ArtistEdge> edges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => (e.FromId == id || e.ToId == id) && e.Kind == EdgeKind.MemberOf)
            .ToListAsync(ct);

        var datedReleases = await _db.Releases
            .AsNoTracking()
            .Where(r => r.ArtistId == id && r.ReleaseDate != null)
            .Select(r => new { r.Id, r.Title, Date = r.ReleaseDate!.Value })
            .ToListAsync(ct);

        LineupTurnover.ReleaseTurnover? pivotal = LineupTurnover.MostPivotal(
            id,
            datedReleases.Select(r => (r.Id, r.Date)).ToList(),
            edges);

        if (pivotal is null)
        {
            return NoContent();
        }

        var byId = datedReleases.ToDictionary(r => r.Id, r => r);
        var release = byId[pivotal.ReleaseId];

        // Resolve the member names for the joined/left sets in one query.
        List<Guid> memberIds = pivotal.Joined.Concat(pivotal.Left).Distinct().ToList();
        Dictionary<Guid, string> names = await _db.Artists
            .AsNoTracking()
            .Where(a => memberIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        List<TurnoverMemberDto> ToMembers(IReadOnlyList<Guid> ids)
        {
            return ids
                .Select(m => new TurnoverMemberDto(m, names.TryGetValue(m, out string? n) ? n : string.Empty))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var dto = new PivotalReleaseDto(
            release.Id,
            release.Title,
            release.Date.Year,
            pivotal.Score,
            ToMembers(pivotal.Joined),
            ToMembers(pivotal.Left));

        return Ok(dto);
    }

    /// <summary>
    /// The tracklist of one release in this band's discography (B5): each recording's position,
    /// title and length in milliseconds, ordered by position. Length is null when MusicBrainz never
    /// timed the track — the front renders an em dash, never a fabricated duration (C7 honesty).
    /// 404 when the release is unknown or does not belong to this artist (no cross-artist leakage).
    /// Returns an empty list for a release whose tracks the import never reached — a designed empty
    /// state (R2), not an error.
    /// </summary>
    [HttpGet("{id:guid}/releases/{releaseId:guid}/tracks")]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> Tracks(Guid id, Guid releaseId, CancellationToken ct = default)
    {
        bool belongs = await _db.Releases
            .AsNoTracking()
            .AnyAsync(r => r.Id == releaseId && r.ArtistId == id, ct);
        if (!belongs)
        {
            return NotFound();
        }

        List<TrackDto> tracks = await _db.Recordings
            .AsNoTracking()
            .Where(rec => rec.ReleaseId == releaseId)
            .OrderBy(rec => rec.Position)
            .Select(rec => new TrackDto(rec.Position, rec.Title, rec.LengthMs))
            .ToListAsync(ct);

        return Ok(tracks);
    }

    /// <summary>
    /// Song-title mining for a band (C21): the lyrical themes its recording titles evoke, most
    /// present first, over a closed bilingual vocabulary (<see cref="TitleLexicon"/>). It is an
    /// <b>approximation</b> from titles — not a curated lyrical fact, and the UI says so (D17). An
    /// empty theme list is honest: the band's titles simply matched no theme word.
    /// </summary>
    [HttpGet("{id:guid}/themes")]
    public async Task<ActionResult<ArtistThemesDto>> Themes(Guid id, CancellationToken ct = default)
    {
        bool exists = await _db.Artists.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        List<string> titles = await _db.Recordings
            .AsNoTracking()
            .Where(rec => _db.Releases.Any(r => r.Id == rec.ReleaseId && r.ArtistId == id))
            .Select(rec => rec.Title)
            .ToListAsync(ct);

        IReadOnlyList<TitleLexicon.ThemeCount> themes = TitleLexicon.CountThemes(titles);

        return Ok(new ArtistThemesDto(
            titles.Count,
            themes.Select(t => new ThemeCountDto(t.Theme, t.Count)).ToList()));
    }

    /// <summary>
    /// The version graph of a band (C10, "quién versionó a quién"): every cross-artist cover that
    /// touches one of this band's recordings — either the band was covered, or the band covered
    /// someone else. Own remixes/remasters (same artist on both ends) are excluded (<see
    /// cref="CoverGraphBuilder.CrossArtist"/>), because they are not the "someone else" story. Nodes
    /// are artists (this band marked "ego"), edges carry the MusicBrainz relation as their label, and
    /// the companion list gives the covered song each edge stands for. Empty for the vast majority of
    /// the underground that no one has covered — a designed empty state (R2).
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<VersionGraphDto>> Versions(Guid id, CancellationToken ct = default)
    {
        bool exists = await _db.Artists.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        // Cover relations where either the original or the covering recording belongs to this band.
        var joined = await (
            from cv in _db.CoverVersions.AsNoTracking()
            join ro in _db.Recordings.AsNoTracking() on cv.OriginalRecordingId equals ro.Id
            join rc in _db.Recordings.AsNoTracking() on cv.CoverRecordingId equals rc.Id
            join relo in _db.Releases.AsNoTracking() on ro.ReleaseId equals relo.Id
            join relc in _db.Releases.AsNoTracking() on rc.ReleaseId equals relc.Id
            where relo.ArtistId == id || relc.ArtistId == id
            select new
            {
                OriginalArtistId = relo.ArtistId,
                CoverArtistId = relc.ArtistId,
                cv.Relation,
                ro.Title,
            }).ToListAsync(ct);

        IReadOnlyList<CoverGraphBuilder.RawCover> crossArtist = CoverGraphBuilder.CrossArtist(
            joined.Select(j => new CoverGraphBuilder.RawCover(j.OriginalArtistId, j.CoverArtistId, j.Relation, j.Title)));

        if (crossArtist.Count == 0)
        {
            return Ok(new VersionGraphDto(new GraphDto([], []), []));
        }

        // Names/kind/rank for every artist on either end of a surviving edge, including this band.
        HashSet<Guid> artistIds = crossArtist
            .SelectMany(c => new[] { c.OriginalArtistId, c.CoverArtistId })
            .ToHashSet();

        Dictionary<Guid, GraphNodeDto> nodes = await _db.Artists
            .AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .Select(a => new GraphNodeDto(a.Id, a.Name, a.Kind, a.Rank, a.Id == id ? "ego" : "node"))
            .ToDictionaryAsync(n => n.Id, ct);

        // Graph edges: one per distinct (original artist, cover artist, relation). The song titles
        // live in the companion list, since the graph cannot carry them.
        List<GraphEdgeDto> edges = crossArtist
            .Select(c => (c.OriginalArtistId, c.CoverArtistId, c.Relation))
            .Distinct()
            .Select(e => new GraphEdgeDto(e.OriginalArtistId, e.CoverArtistId, "cover", e.Relation))
            .ToList();

        List<GraphNodeDto> orderedNodes = nodes.Values
            .OrderByDescending(n => n.Role == "ego")
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<CoverEdgeDto> versions = crossArtist
            .Select(c => new CoverEdgeDto(
                c.OriginalArtistId,
                nodes.TryGetValue(c.OriginalArtistId, out GraphNodeDto? on) ? on.Name : string.Empty,
                c.CoverArtistId,
                nodes.TryGetValue(c.CoverArtistId, out GraphNodeDto? cn) ? cn.Name : string.Empty,
                c.Relation,
                c.Title))
            .OrderBy(v => v.OriginalArtistName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.CoverArtistName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new VersionGraphDto(new GraphDto(orderedNodes, edges), versions));
    }
}
