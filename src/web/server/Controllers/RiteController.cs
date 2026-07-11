using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The Rite: cold start, blind serving, the proxied audio, and Summon/Banish/Again (SPEC §5.3,
/// §5.6, §5.7, §6). Everything except the audio sub-resource requires a signed-in user; the
/// audio endpoint is a capability URL (the rite id is an unguessable token) so a plain
/// <c>&lt;audio&gt;</c> element can stream it without leaking the origin preview URL.
/// </summary>
[ApiController]
[Route("api/rite")]
[Authorize]
public class RiteController : ControllerBase
{
    private const int MaxSeedArtists = 20;
    private const int DefaultLastFmTop = 40;

    private readonly GrimoireDbContext _db;
    private readonly RiteEngine _engine;
    private readonly ArtistDetailBuilder _details;
    private readonly PreviewAudioProxy _audio;
    private readonly IColdStartImport _lastFm;
    private readonly ILogger<RiteController> _logger;

    public RiteController(
        GrimoireDbContext db,
        RiteEngine engine,
        ArtistDetailBuilder details,
        PreviewAudioProxy audio,
        IColdStartImport lastFm,
        ILogger<RiteController> logger)
    {
        _db = db;
        _engine = engine;
        _details = details;
        _audio = audio;
        _lastFm = lastFm;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Cold start (DECISIONS D15)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Bands to pick from on the cold-start "choose five" screen. NOT blind — the user is choosing
    /// bands they already know. Only bands with an embedding qualify (their vector seeds the taste);
    /// the more prolific ones surface first so the list is recognisable.
    /// </summary>
    [HttpGet("seed-candidates")]
    public async Task<ActionResult<IReadOnlyList<SeedCandidateDto>>> SeedCandidates(
        [FromQuery] int limit = 60,
        CancellationToken ct = default)
    {
        int take = Math.Clamp(limit, 1, 200);

        List<SeedCandidateDto> candidates = await _db.Artists
            .Where(a => a.Embedding != null)
            .OrderByDescending(a => a.Releases.Count)
            .ThenBy(a => a.Name)
            .Take(take)
            .Select(a => new SeedCandidateDto(a.Id, a.Name, a.Country, a.FormedYear))
            .ToListAsync(ct);

        return Ok(candidates);
    }

    /// <summary>
    /// Seeds a user's taste from the bands they picked (DECISIONS D15): the taste vector is the
    /// mean of the chosen artists' embeddings. Those embeddings are ALREADY centred (D26), so the
    /// mean is centred too — the corpus mean is never subtracted again here.
    /// </summary>
    [HttpPost("seed")]
    public async Task<ActionResult<TasteStatusDto>> Seed(SeedRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        List<Guid> ids = request.ArtistIds.Distinct().ToList();

        if (ids.Count == 0 || ids.Count > MaxSeedArtists)
        {
            return BadRequest(new { message = $"Pick between 1 and {MaxSeedArtists} bands to seed your taste." });
        }

        // Only embeddings that exist can be averaged; a picked band without one is reported, not
        // silently dropped, so the caller knows its seed is thinner than it asked for.
        List<float[]> embeddings = (await _db.Artists
                .Where(a => ids.Contains(a.Id) && a.Embedding != null)
                .Select(a => a.Embedding!)
                .ToListAsync(ct))
            .Select(v => v.ToArray())
            .ToList();

        if (embeddings.Count == 0)
        {
            return BadRequest(new { message = "None of the picked bands has an embedding yet; taste cannot be seeded from them." });
        }

        float[] taste = TasteMath.Seed(embeddings);

        UserTaste row = await UpsertTasteAsync(userId, ct);
        row.Embedding = new Vector(taste);
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(await TasteStatusAsync(userId, ct));
    }

    /// <summary>
    /// Cold start from Last.fm scrobbles (feature C1). BLOCKED without a Last.fm API key (Q5): when
    /// the source is disabled the endpoint says so plainly (503) rather than inventing scrobbles.
    /// When a key is configured, it maps the user's top artists onto the catalogue and seeds taste
    /// from their centred embeddings.
    /// </summary>
    [HttpPost("import-lastfm")]
    public async Task<ActionResult<TasteStatusDto>> ImportLastFm(LastFmImportRequest request, CancellationToken ct)
    {
        if (!_lastFm.Enabled)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Last.fm import is unavailable: no Last.fm API key is configured (blocker Q5). Seed your taste by choosing bands instead." });
        }

        Guid userId = CurrentUserId();

        IReadOnlyList<string>? topArtists = await _lastFm.GetTopArtistNamesAsync(request.Username, DefaultLastFmTop, ct);

        if (topArtists is null || topArtists.Count == 0)
        {
            return NotFound(new { message = "Last.fm returned no top artists for that username." });
        }

        // Map Last.fm names onto the catalogue by exact normalised name (the same matcher the ETL
        // uses — DECISIONS D25 — so a mismatch drops the band rather than seeding the wrong one).
        var catalogue = await _db.Artists
            .Where(a => a.Embedding != null)
            .Select(a => new { a.Name, a.Embedding })
            .ToListAsync(ct);

        HashSet<string> wanted = topArtists.Select(NameMatch.Normalize).ToHashSet();

        List<float[]> embeddings = catalogue
            .Where(a => wanted.Contains(NameMatch.Normalize(a.Name)))
            .Select(a => a.Embedding!.ToArray())
            .ToList();

        if (embeddings.Count == 0)
        {
            return NotFound(new { message = "None of your top Last.fm artists is in the catalogue yet." });
        }

        float[] taste = TasteMath.Seed(embeddings);

        UserTaste row = await UpsertTasteAsync(userId, ct);
        row.Embedding = new Vector(taste);
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(await TasteStatusAsync(userId, ct));
    }

    /// <summary>Whether the caller already has a taste, so the front knows whether to run cold start.</summary>
    [HttpGet("taste")]
    public async Task<ActionResult<TasteStatusDto>> Taste(CancellationToken ct)
    {
        return Ok(await TasteStatusAsync(CurrentUserId(), ct));
    }

    // -----------------------------------------------------------------------
    // Serving The Rite (features B13, B14, C13)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Serves one band blind (SPEC §5.3). The response carries no name, genre, country or cover —
    /// only the capability token, the risk, and the proxied audio URL. Returns 409 if the caller
    /// has no taste yet (run cold start first) and 204 when the ring is empty (a designed empty
    /// state, not an error).
    /// </summary>
    [HttpPost("serve")]
    public async Task<ActionResult<ServedRiteDto>> Serve(ServeRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        UserTaste? taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (taste?.Embedding is null)
        {
            return Conflict(new { message = "No taste yet. Seed it by choosing bands or importing Last.fm before starting a rite." });
        }

        RiteFilters filters = new(request.Country, request.DecadeFrom, request.DecadeTo);

        RiteCandidate? candidate = await _engine.FindAsync(
            userId,
            taste.Embedding,
            taste.Repulsion,
            request.Comfort,
            filters,
            ct);

        if (candidate is null)
        {
            // Nothing in the ring: the servable pool is small (DECISIONS D25) and a tight slider or
            // hard filter can empty it. The front shows a designed empty state.
            return NoContent();
        }

        Rite rite = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ArtistId = candidate.ArtistId,
            State = RiteState.Served,
            Risk = (float)candidate.RiskPercentile,
            ServedAt = DateTimeOffset.UtcNow,
        };

        _db.Rites.Add(rite);
        await _db.SaveChangesAsync(ct);

        string audioUrl = $"{Request.Scheme}://{Request.Host}/api/rite/{rite.Id}/audio";

        return Ok(new ServedRiteDto(rite.Id, candidate.RiskPercentile, audioUrl));
    }

    /// <summary>
    /// Streams the served band's preview through the server (SPEC §5.3). Capability URL: the token
    /// is the rite id, so no auth header is needed and a plain audio element can play it, while the
    /// origin preview URL — which usually embeds the band name — never reaches the client.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{token:guid}/audio")]
    public async Task<IActionResult> Audio(Guid token, CancellationToken ct)
    {
        string? previewUrl = await _db.Rites
            .Where(r => r.Id == token)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a.PreviewUrl)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(previewUrl))
        {
            return NotFound();
        }

        HttpResponseMessage? upstream = await _audio.OpenAsync(previewUrl, ct);

        if (upstream is null)
        {
            return NotFound();
        }

        // Let the framework dispose the upstream message once the response is written.
        HttpContext.Response.RegisterForDispose(upstream);

        Stream body = await upstream.Content.ReadAsStreamAsync(ct);
        string contentType = upstream.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";

        return File(body, contentType);
    }

    // -----------------------------------------------------------------------
    // Resolving a rite: Summon / Banish / Again (features B13, C3, C4)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves a served rite. Summon pulls the taste toward the band and reveals it; Banish pushes
    /// the repulsion toward it and keeps it blind; Again is a neutral skip that keeps it blind. The
    /// band is revealed only on Summon (SPEC B13), with the "why you were served this" explanation
    /// (feature C4).
    /// </summary>
    [HttpPost("{token:guid}/resolve")]
    public async Task<ActionResult<ResolveResultDto>> Resolve(Guid token, ResolveRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        if (!TryParseAction(request.Action, out RiteState target))
        {
            return BadRequest(new { message = "Action must be 'summon', 'banish' or 'again'." });
        }

        Rite? rite = await _db.Rites.FirstOrDefaultAsync(r => r.Id == token && r.UserId == userId, ct);

        if (rite is null)
        {
            return NotFound(new { message = "No such rite for this user." });
        }

        if (rite.State != RiteState.Served)
        {
            return Conflict(new { message = "This rite has already been resolved." });
        }

        var artistRow = await _db.Artists
            .Where(a => a.Id == rite.ArtistId)
            .Select(a => new { a.Embedding, a.Rank })
            .FirstOrDefaultAsync(ct);

        float[]? artistEmbedding = artistRow?.Embedding?.ToArray();
        Rank? artistRank = artistRow?.Rank;

        UserTaste taste = await UpsertTasteAsync(userId, ct);

        // Update the taste (summon) or repulsion (banish). Again touches neither. A served band
        // always has an embedding (the pool requires it), but we guard rather than assume.
        if (artistEmbedding is not null)
        {
            switch (target)
            {
                case RiteState.Summoned:
                    taste.Embedding = new Vector(TasteMath.ApplySummon(taste.Embedding?.ToArray(), artistEmbedding));
                    break;

                case RiteState.Banished:
                    taste.Repulsion = new Vector(TasteMath.ApplyBanish(taste.Repulsion?.ToArray(), artistEmbedding));
                    break;

                case RiteState.Again:
                default:
                    break;
            }

            taste.UpdatedAt = DateTimeOffset.UtcNow;
        }

        rite.State = target;
        rite.ResolvedAt = DateTimeOffset.UtcNow;

        // Depth Score (feature B15): recompute on every summon over everything the user has summoned,
        // this band included, awarding more for rarer finds. The current rite is still Served in the
        // DB (its state change is not yet saved), so we sum the other summoned bands' ranks and add
        // this band's rank. A null rank scores nothing — no rank is invented (DECISIONS D33).
        if (target == RiteState.Summoned)
        {
            List<Rank?> summonedRanks = await _db.Rites
                .Where(r => r.UserId == userId && r.State == RiteState.Summoned && r.ArtistId != rite.ArtistId)
                .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a.Rank)
                .ToListAsync(ct);

            summonedRanks.Add(artistRank);
            taste.DepthScore = DepthScore.Compute(summonedRanks);
        }

        await _db.SaveChangesAsync(ct);

        // Reveal only on summon: the reward. Banish and again stay blind on purpose (C3/C20).
        RiteRevealDto? reveal = null;
        if (target == RiteState.Summoned)
        {
            reveal = await BuildRevealAsync(userId, rite.ArtistId, taste.Embedding?.ToArray(), artistEmbedding, taste.DepthScore, ct);
        }

        return Ok(new ResolveResultDto(target, reveal));
    }

    // -----------------------------------------------------------------------
    // The grimoire: rites in state Summoned (SPEC §10 — "the grimoire is not a table")
    // -----------------------------------------------------------------------

    /// <summary>The bands the user has summoned, newest first — their grimoire (feature C17 data).</summary>
    [HttpGet("grimoire")]
    public async Task<ActionResult<IReadOnlyList<GrimoireEntryDto>>> Grimoire(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        List<GrimoireEntryDto> entries = await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
            .OrderByDescending(r => r.ResolvedAt)
            .Join(
                _db.Artists,
                r => r.ArtistId,
                a => a.Id,
                (r, a) => new GrimoireEntryDto(
                    new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank),
                    r.ResolvedAt!.Value))
            .ToListAsync(ct);

        return Ok(entries);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<RiteRevealDto?> BuildRevealAsync(
        Guid userId,
        Guid artistId,
        float[]? taste,
        float[]? artistEmbedding,
        int depthScore,
        CancellationToken ct)
    {
        ArtistDetailDto? artist = await _details.BuildAsync(artistId, ct);

        if (artist is null)
        {
            return null;
        }

        double distance = taste is not null && artistEmbedding is not null
            ? VectorMath.CosineDistance(taste, artistEmbedding)
            : double.NaN;

        // The bands already in the grimoire, to explain the connection (feature C4).
        List<Guid> summonedIds = await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned && r.ArtistId != artistId)
            .Select(r => r.ArtistId)
            .ToListAsync(ct);

        List<string> sharedTags = [];
        List<string> sharedMembers = [];

        if (summonedIds.Count > 0)
        {
            HashSet<string> grimoireTags = (await _db.Artists
                    .Where(a => summonedIds.Contains(a.Id))
                    .Select(a => a.Tags)
                    .ToListAsync(ct))
                .SelectMany(t => t)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            sharedTags = artist.Tags.Where(grimoireTags.Contains).ToList();

            // Members shared between this band and the grimoire (member_of: member is the "from").
            HashSet<Guid> thisMembers = (await _db.ArtistEdges
                    .Where(e => e.Kind == EdgeKind.MemberOf && e.ToId == artistId)
                    .Select(e => e.FromId)
                    .ToListAsync(ct))
                .ToHashSet();

            List<Guid> grimoireMemberIds = await _db.ArtistEdges
                .Where(e => e.Kind == EdgeKind.MemberOf && summonedIds.Contains(e.ToId))
                .Select(e => e.FromId)
                .ToListAsync(ct);

            List<Guid> intersection = grimoireMemberIds.Where(thisMembers.Contains).Distinct().ToList();

            if (intersection.Count > 0)
            {
                sharedMembers = await _db.Artists
                    .Where(a => intersection.Contains(a.Id))
                    .Select(a => a.Name)
                    .ToListAsync(ct);
            }
        }

        return new RiteRevealDto(artist, new RiteExplanationDto(distance, sharedTags, sharedMembers), depthScore);
    }

    private async Task<UserTaste> UpsertTasteAsync(Guid userId, CancellationToken ct)
    {
        UserTaste? row = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (row is null)
        {
            row = new UserTaste { UserId = userId, UpdatedAt = DateTimeOffset.UtcNow };
            _db.UserTastes.Add(row);
        }

        return row;
    }

    private async Task<TasteStatusDto> TasteStatusAsync(Guid userId, CancellationToken ct)
    {
        UserTaste? taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        int summoned = await _db.Rites
            .CountAsync(r => r.UserId == userId && r.State == RiteState.Summoned, ct);

        return new TasteStatusDto(taste?.Embedding is not null, summoned, taste?.UpdatedAt, taste?.DepthScore ?? 0);
    }

    private static bool TryParseAction(string action, out RiteState state)
    {
        switch (action?.Trim().ToLowerInvariant())
        {
            case "summon":
                state = RiteState.Summoned;
                return true;
            case "banish":
                state = RiteState.Banished;
                return true;
            case "again":
                state = RiteState.Again;
                return true;
            default:
                state = default;
                return false;
        }
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
