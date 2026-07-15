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
using Pgvector.EntityFrameworkCore;

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

    /// <summary>
    /// How many distinct ring candidates a serve draws so it can skip the inaudible ones (DECISIONS
    /// D25: ~48 % of the underground is insonorizable) and the just-in-time resolver still finds a
    /// band that sounds. Also caps how many previews one serve resolves online, bounding its latency.
    /// </summary>
    private const int ServeCandidatePool = 12;

    private readonly GrimoireDbContext _db;
    private readonly RiteEngine _engine;
    private readonly ArtistDetailBuilder _details;
    private readonly PreviewAudioProxy _audio;
    private readonly PreviewResolver _previews;
    private readonly IColdStartImport _lastFm;
    private readonly GrimoireCrossService _cross;
    private readonly NotificationService _notifications;
    private readonly ILogger<RiteController> _logger;

    public RiteController(
        GrimoireDbContext db,
        RiteEngine engine,
        ArtistDetailBuilder details,
        PreviewAudioProxy audio,
        PreviewResolver previews,
        IColdStartImport lastFm,
        GrimoireCrossService cross,
        NotificationService notifications,
        ILogger<RiteController> logger)
    {
        _db = db;
        _engine = engine;
        _details = details;
        _audio = audio;
        _previews = previews;
        _lastFm = lastFm;
        _cross = cross;
        _notifications = notifications;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Cold start (DECISIONS D15)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Bands to pick from on the cold-start "choose five" screen. NOT blind — the user is choosing
    /// bands they already know. Only bands with an embedding qualify (their vector seeds the taste)
    /// and only bands Last.fm has heard of (<c>listeners</c> is not null), because a band nobody has
    /// heard cannot be recognised on a pick screen.
    ///
    /// <para>
    /// The grid is a fair round-robin across the families (metal, rock, punk, classical, folk,
    /// electronic), drawn from the most-LISTENED bands of each. Ranking the whole catalogue by how
    /// prolific a band is instead buries the metal under the classical canon — Bach has 5 804 releases
    /// and Metallica 1 035, so a "most releases first" grid is a wall of composers.
    /// </para>
    ///
    /// <para>
    /// This grid is the <b>stable</b> part of the screen: it is fetched once and never reshuffles.
    /// Picking a band grows it in place through <see cref="RelatedSeeds"/> instead — see there for why.
    /// </para>
    /// </summary>
    [HttpGet("seed-candidates")]
    public async Task<ActionResult<IReadOnlyList<SeedCandidateDto>>> SeedCandidates(
        [FromQuery] int limit = 60,
        CancellationToken ct = default)
    {
        int take = Math.Clamp(limit, 1, 200);

        return Ok(await StarterGridAsync(take, ct));
    }

    /// <summary>
    /// The bands nearest to one band in embedding space, for the cold-start grid to unfold underneath
    /// it when it is picked: choose Judas Priest and Black Sabbath, Iron Maiden and the NWOBHM appear
    /// directly below; choose Bach and the classical does.
    ///
    /// <para>
    /// This is a per-band expansion, deliberately NOT a re-ranking of the whole grid around the picks.
    /// Reshuffling the grid on every pick means a band chosen in the seventh row shifts everything
    /// above it, and the user has to re-read the screen from the top after each click. Growing the
    /// grid downward keeps everything already read exactly where it was.
    /// </para>
    ///
    /// <para>
    /// It is also, for the same reason, one band's neighbours and never the mean of several: the
    /// midpoint between a heavy metal vector and a baroque one is a region that sounds like neither.
    /// The caller drops any band it is already showing (it knows its own grid; the server does not).
    /// </para>
    /// </summary>
    [HttpGet("seed-candidates/{artistId:guid}/related")]
    public async Task<ActionResult<IReadOnlyList<SeedCandidateDto>>> RelatedSeeds(
        Guid artistId,
        [FromQuery] int limit = 24,
        CancellationToken ct = default)
    {
        int take = Math.Clamp(limit, 1, 60);

        Vector? seed = await _db.Artists
            .Where(a => a.Id == artistId)
            .Select(a => a.Embedding)
            .FirstOrDefaultAsync(ct);

        // A band with no embedding has no neighbourhood. That is an empty answer, not an error and
        // certainly not a grid of unrelated bands dressed up as related ones.
        if (seed is null)
        {
            return Ok(Array.Empty<SeedCandidateDto>());
        }

        List<SeedCandidateDto> related = await _db.Artists
            .Discoverable()
            .Where(a => a.Listeners != null && a.Id != artistId)
            .OrderBy(a => a.Embedding!.CosineDistance(seed))
            .Take(take)
            .Select(a => new SeedCandidateDto(a.Id, a.Name, a.Country, a.FormedYear))
            .ToListAsync(ct);

        return Ok(related);
    }

    /// <summary>The balanced starter grid: the most-listened bands of each family, taken in turn.</summary>
    private async Task<List<SeedCandidateDto>> StarterGridAsync(int take, CancellationToken ct)
    {
        if (take <= 0)
        {
            return [];
        }

        // One pass over the most-listened bands, classified into families in memory. The pool is cut
        // deep enough that even a thin family (punk, folk) fills its lane at the default grid size.
        const int StarterPool = 1200;

        var pool = await _db.Artists
            .Discoverable()
            .Where(a => a.Listeners != null)
            .OrderByDescending(a => a.Listeners)
            .Take(StarterPool)
            .Select(a => new { a.Id, a.Name, a.Country, a.FormedYear, a.Tags })
            .ToListAsync(ct);

        List<IReadOnlyList<SeedCandidateDto>> lanes = SeedPool.StarterFamilies
            .Select(family => (IReadOnlyList<SeedCandidateDto>)pool
                .Where(a => SeedPool.FamilyOf(a.Tags) == family)
                .Select(a => new SeedCandidateDto(a.Id, a.Name, a.Country, a.FormedYear))
                .ToList())
            .ToList();

        return SeedPool.Interleave(lanes, take, c => c.Id);
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

        // Snapshot the seed as the origin of the taste trajectory (feature C16).
        AddSnapshot(userId, row);

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
            .Discoverable()
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

        // Snapshot the imported taste as the origin of the trajectory (feature C16).
        AddSnapshot(userId, row);

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
    /// The optional genre lanes a rite can be narrowed to (feature added 2026-07-15). Public and
    /// static — the front renders them as an optional picker; choosing none keeps the rite fully
    /// blind and open, the default. Still blind either way (supersedes D43, see DECISIONS).
    /// </summary>
    [HttpGet("genres")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<RiteGenreDto>> Genres()
    {
        return Ok(RiteGenres.All.Select(g => new RiteGenreDto(g.Key, g.Label)).ToList());
    }

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

        // The tag lane: a raw clicked tag (GenreNeedle) takes precedence over a catalogue key (Genre);
        // both feed the SAME tag lane. The theme lane is orthogonal and ANDed on top when present. All
        // are optional — with none set the rite is fully open, exactly as before. The rite stays blind.
        string? tagNeedle = SearchNeedle.Clean(request.GenreNeedle) ?? RiteGenres.NeedleFor(request.Genre);

        RiteFilters filters = new(
            request.Country,
            request.DecadeFrom,
            request.DecadeTo,
            tagNeedle,
            SearchNeedle.Clean(request.ThemeNeedle),
            request.ThemeKind);

        // Draw several ring candidates, not one: the ring is now the embedded catalogue (audibility is
        // no longer pre-filtered — DECISIONS D25/D19), so we resolve the preview just-in-time and skip
        // the inaudible bands until one sounds.
        IReadOnlyList<RiteCandidate> candidates = await _engine.FindManyAsync(
            userId,
            taste.Embedding,
            taste.Repulsion,
            request.Comfort,
            filters,
            ServeCandidatePool,
            ct);

        if (candidates.Count == 0)
        {
            // Nothing in the ring at all: a tight slider or hard filter emptied it. The front shows a
            // designed empty state (not an error).
            return NoContent();
        }

        RiteCandidate? candidate = await FirstAudibleAsync(candidates, ct);

        if (candidate is null)
        {
            // The ring had bands, but none of the ones we probed could be made to sound (the JIT
            // resolver returned null for each — genuinely inaudible, DECISIONS D25). Designed empty
            // state, still not an error.
            return NoContent();
        }

        // An abandoned Served rite (served but never Summoned/Banished/Again) must not lock its band
        // out of the pool forever (DECISIONS D39). FindManyAsync already ran with the old rows present,
        // so the just-abandoned band is not re-served this turn; it becomes eligible on a later serve.
        await PurgeAbandonedServedAsync(userId, ct);

        Rite rite = NewServed(userId, candidate);
        _db.Rites.Add(rite);
        await _db.SaveChangesAsync(ct);

        return Ok(new ServedRiteDto(rite.Id, candidate.RiskPercentile, AudioUrlFor(rite.Id)));
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

        // The Depth Score before this summon recomputes it, so we can tell whether the summon lifted
        // the summoner over any friend (the rarity-surpassed notification below).
        int oldDepth = taste.DepthScore;

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

            // A summon moved the taste vector: snapshot the new position on the trajectory (C16).
            AddSnapshot(userId, taste);
        }

        await _db.SaveChangesAsync(ct);

        // A summon that lifted the summoner's Depth Score may have carried them past a friend: tell
        // any friend they just overtook (feature: "a friend surpassed you in rarity"). Best-effort and
        // AFTER the resolve is committed — a notification hiccup must never fail or slow the summon.
        if (target == RiteState.Summoned && taste.DepthScore > oldDepth)
        {
            await NotifyRaritySurpassedAsync(userId, oldDepth, taste.DepthScore, ct);
        }

        // Reveal only on summon: the reward. Banish and again stay blind on purpose (C3/C20).
        RiteRevealDto? reveal = null;
        if (target == RiteState.Summoned)
        {
            reveal = await BuildRevealAsync(userId, rite.ArtistId, taste.Embedding?.ToArray(), artistEmbedding, taste.DepthScore, ct);
        }

        return Ok(new ResolveResultDto(target, reveal));
    }

    // -----------------------------------------------------------------------
    // The blind duel (feature C2, DECISIONS D16)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts a blind duel (feature C2): two bands drawn from the same ring, both served blind. The
    /// user listens to each and picks one; the pairwise preference (Bradley-Terry) teaches the taste
    /// vector more than a lone like. Returns 409 without a taste (run cold start first) and 204 when
    /// the ring cannot supply two distinct bands (a designed empty state on a small pool — D25).
    /// </summary>
    [HttpPost("duel")]
    public async Task<ActionResult<DuelServedDto>> Duel(DuelRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        UserTaste? taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (taste?.Embedding is null)
        {
            return Conflict(new { message = "No taste yet. Seed it by choosing bands or importing Last.fm before a duel." });
        }

        RiteFilters filters = new(request.Country, request.DecadeFrom, request.DecadeTo);

        // Draw a pool and keep the first two that can be made to sound (JIT preview resolution —
        // DECISIONS D25/D19), the same way a single serve does.
        IReadOnlyList<RiteCandidate> candidates = await _engine.FindManyAsync(
            userId, taste.Embedding, taste.Repulsion, request.Comfort, filters, ServeCandidatePool, ct);

        List<RiteCandidate> pair = await SelectAudibleAsync(candidates, 2, ct);

        if (pair.Count < 2)
        {
            // The ring could not offer two audible bands: too tight a slider, everything this close
            // already judged, or the probed candidates were all inaudible. Designed empty state (D25).
            return NoContent();
        }

        // A duel supersedes any dangling Served rites (DECISIONS D39): FindManyAsync ran with the old
        // rows present, so the just-abandoned bands are not re-served this turn.
        await PurgeAbandonedServedAsync(userId, ct);

        Rite left = NewServed(userId, pair[0]);
        Rite right = NewServed(userId, pair[1]);
        _db.Rites.Add(left);
        _db.Rites.Add(right);
        await _db.SaveChangesAsync(ct);

        return Ok(new DuelServedDto(SideOf(left), SideOf(right)));
    }

    /// <summary>
    /// Resolves a duel (feature C2): the winner the user preferred over the loser. The taste vector
    /// moves toward the winner and away from the loser (Bradley-Terry, DuelMath), the winner enters
    /// the grimoire (Summoned) and is revealed, and the loser is set aside (Again — seen, excluded,
    /// but NOT banished: the user did not reject it, only preferred the other). Both tokens must be
    /// the caller's own unresolved served rites. 400 if the tokens are equal, 404 if either is not
    /// found, 409 if either was already resolved.
    /// </summary>
    [HttpPost("duel/resolve")]
    public async Task<ActionResult<DuelResultDto>> ResolveDuel(DuelResolveRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        if (request.WinnerToken == request.LoserToken)
        {
            return BadRequest(new { message = "A duel needs two different bands." });
        }

        Rite? winner = await _db.Rites.FirstOrDefaultAsync(r => r.Id == request.WinnerToken && r.UserId == userId, ct);
        Rite? loser = await _db.Rites.FirstOrDefaultAsync(r => r.Id == request.LoserToken && r.UserId == userId, ct);

        if (winner is null || loser is null)
        {
            return NotFound(new { message = "One or both duel rites do not exist for this user." });
        }

        if (winner.State != RiteState.Served || loser.State != RiteState.Served)
        {
            return Conflict(new { message = "This duel has already been resolved." });
        }

        var winnerRow = await _db.Artists
            .Where(a => a.Id == winner.ArtistId)
            .Select(a => new { a.Embedding, a.Rank })
            .FirstOrDefaultAsync(ct);

        float[]? loserEmbedding = (await _db.Artists
                .Where(a => a.Id == loser.ArtistId)
                .Select(a => a.Embedding)
                .FirstOrDefaultAsync(ct))
            ?.ToArray();

        float[]? winnerEmbedding = winnerRow?.Embedding?.ToArray();
        Rank? winnerRank = winnerRow?.Rank;

        UserTaste taste = await UpsertTasteAsync(userId, ct);

        // Bradley-Terry: pull the taste toward the winner and push it away from the loser (D16). Both
        // embeddings are already centred (D26) — DuelMath only blends, it never re-centres. A served
        // band always has an embedding (the pool requires it), but we guard rather than assume.
        if (winnerEmbedding is not null && loserEmbedding is not null)
        {
            taste.Embedding = new Vector(DuelMath.ApplyDuel(taste.Embedding?.ToArray(), winnerEmbedding, loserEmbedding));
            taste.UpdatedAt = DateTimeOffset.UtcNow;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        winner.State = RiteState.Summoned;
        winner.ResolvedAt = now;
        loser.State = RiteState.Again;
        loser.ResolvedAt = now;

        // Depth Score (feature B15): recompute over everything summoned, the winner included. The
        // winner is still Served in the DB (its state change is unsaved), so we sum the other summoned
        // bands' ranks and add the winner's. A null rank scores nothing — no rank is invented (D36).
        List<Rank?> summonedRanks = await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned && r.ArtistId != winner.ArtistId)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a.Rank)
            .ToListAsync(ct);

        summonedRanks.Add(winnerRank);
        taste.DepthScore = DepthScore.Compute(summonedRanks);

        // The taste vector moved: snapshot the new position on the trajectory (C16).
        AddSnapshot(userId, taste);

        await _db.SaveChangesAsync(ct);

        RiteRevealDto? reveal = await BuildRevealAsync(
            userId, winner.ArtistId, taste.Embedding?.ToArray(), winnerEmbedding, taste.DepthScore, ct);

        if (reveal is null)
        {
            return NotFound(new { message = "The winning band could not be revealed." });
        }

        return Ok(new DuelResultDto(reveal));
    }

    // -----------------------------------------------------------------------
    // Guess the decade (feature C27)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Serves one blind band for the decade game (feature C27). Same blind serve as the rite, but
    /// the pool is narrowed to scorable bands (formed year, country and at least one tag) so every
    /// bet is judged against real data. 409 without a taste; 204 when no scorable band is in reach.
    /// </summary>
    [HttpPost("decade")]
    public async Task<ActionResult<DecadeServedDto>> ServeDecade(DecadeServeRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        UserTaste? taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (taste?.Embedding is null)
        {
            return Conflict(new { message = "No taste yet. Seed it by choosing bands or importing Last.fm before the decade game." });
        }

        IReadOnlyList<RiteCandidate> candidates = await _engine.FindManyAsync(
            userId, taste.Embedding, taste.Repulsion, request.Comfort, new RiteFilters(null, null, null),
            ServeCandidatePool, ct, scorableOnly: true);

        if (candidates.Count == 0)
        {
            return NoContent();
        }

        RiteCandidate? candidate = await FirstAudibleAsync(candidates, ct);

        if (candidate is null)
        {
            return NoContent();
        }

        await PurgeAbandonedServedAsync(userId, ct);

        Rite rite = NewServed(userId, candidate);
        _db.Rites.Add(rite);
        await _db.SaveChangesAsync(ct);

        return Ok(new DecadeServedDto(rite.Id, AudioUrlFor(rite.Id)));
    }

    /// <summary>
    /// Scores a decade-game bet (feature C27) and reveals the band. The player bets a decade, a
    /// country and a subgenre; each is scored against the band's real data (DecadeScore). The decade
    /// game trains the ear — it does NOT move the taste vector (a bet is not a preference) — so the
    /// band is set aside (Again: seen and excluded, never banished). The scoreboard is accumulated in
    /// the session by the front. 404 if the rite is not the caller's; 409 if it was already resolved.
    /// </summary>
    [HttpPost("{token:guid}/guess")]
    public async Task<ActionResult<DecadeScoreDto>> Guess(Guid token, DecadeGuessRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        Rite? rite = await _db.Rites.FirstOrDefaultAsync(r => r.Id == token && r.UserId == userId, ct);

        if (rite is null)
        {
            return NotFound(new { message = "No such rite for this user." });
        }

        if (rite.State != RiteState.Served)
        {
            return Conflict(new { message = "This band has already been revealed." });
        }

        var truth = await _db.Artists
            .Where(a => a.Id == rite.ArtistId)
            .Select(a => new { a.FormedYear, a.Country, a.Tags })
            .FirstOrDefaultAsync(ct);

        if (truth is null)
        {
            return NotFound(new { message = "The served band no longer exists." });
        }

        RoundScore score = DecadeScore.Score(
            new DecadeGuess(request.Decade, request.Country, request.Subgenre),
            new DecadeTruth(truth.FormedYear, truth.Country, truth.Tags));

        // Seen and scored: set aside (Again). No taste change — ear training is not a preference.
        rite.State = RiteState.Again;
        rite.ResolvedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        ArtistDetailDto? artist = await _details.BuildAsync(rite.ArtistId, ct);

        if (artist is null)
        {
            return NotFound(new { message = "The band could not be revealed." });
        }

        string actualDecade = truth.FormedYear is int year ? $"{DecadeScore.DecadeOf(year)}s" : "—";
        string actualTags = truth.Tags.Length > 0 ? string.Join(", ", truth.Tags) : "—";

        DecadeScoreDto dto = new(
            artist,
            new DecadeDimensionDto($"{DecadeScore.DecadeOf(request.Decade)}s", actualDecade, Outcome(score.Decade), score.Decade.Points),
            new DecadeDimensionDto(request.Country ?? string.Empty, truth.Country ?? "—", Outcome(score.Country), score.Country.Points),
            new DecadeDimensionDto(request.Subgenre ?? string.Empty, actualTags, Outcome(score.Subgenre), score.Subgenre.Points),
            score.Total,
            RoundScore.MaxPoints);

        return Ok(dto);
    }

    private static string Outcome(DimensionScore dimension)
    {
        return dimension.Outcome.ToString().ToLowerInvariant();
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
    // Crossed grimoires (feature C23)
    // -----------------------------------------------------------------------

    /// <summary>The caller's own grimoire code (their user id) — what a friend pastes to cross grimoires (C23).</summary>
    [HttpGet("grimoire/code")]
    public ActionResult<GrimoireCodeDto> GrimoireCode()
    {
        return Ok(new GrimoireCodeDto(CurrentUserId().ToString()));
    }

    /// <summary>
    /// Crosses the caller's grimoire with another user's (C23): what they have that you lack, what
    /// you have that they lack, and the common ground. 400 for your own code; 404 when the other
    /// grimoire code is not a real user.
    /// </summary>
    [HttpGet("grimoire/compare")]
    public async Task<ActionResult<CrossedGrimoiresDto>> CompareGrimoires([FromQuery] Guid other, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        if (other == Guid.Empty || other == userId)
        {
            return BadRequest(new { message = "Paste a friend's grimoire code, not your own." });
        }

        if (!await _db.Users.AnyAsync(u => u.Id == other, ct))
        {
            return NotFound(new { message = "No grimoire answers to that code." });
        }

        return Ok(await _cross.CrossAsync(userId, other, ct));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>A fresh Served rite for a chosen candidate (shared by serve, duel and the decade game).</summary>
    private Rite NewServed(Guid userId, RiteCandidate candidate)
    {
        return new Rite
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ArtistId = candidate.ArtistId,
            State = RiteState.Served,
            Risk = (float)candidate.RiskPercentile,
            ServedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>The capability audio URL for a rite id (the origin preview URL never reaches the client).</summary>
    private string AudioUrlFor(Guid riteId)
    {
        return $"{Request.Scheme}://{Request.Host}/api/rite/{riteId}/audio";
    }

    /// <summary>One blind side of a duel: its token and its proxied audio URL. No name, no origin.</summary>
    private DuelSideDto SideOf(Rite rite)
    {
        return new DuelSideDto(rite.Id, AudioUrlFor(rite.Id));
    }

    /// <summary>
    /// Drops the user's unresolved Served rites (DECISIONS D39): an abandoned serve carries no signal
    /// and must not lock its band out of the small servable pool (D25) forever. Callers run their
    /// FindAsync/FindManyAsync BEFORE this so the just-abandoned band is not re-served the same turn.
    /// </summary>
    private async Task PurgeAbandonedServedAsync(Guid userId, CancellationToken ct)
    {
        List<Rite> abandoned = await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Served)
            .ToListAsync(ct);

        if (abandoned.Count > 0)
        {
            _db.Rites.RemoveRange(abandoned);
        }
    }

    /// <summary>The one audible band for a serve/decade round, or null when none of the drawn candidates can sound.</summary>
    private async Task<RiteCandidate?> FirstAudibleAsync(IReadOnlyList<RiteCandidate> candidates, CancellationToken ct)
    {
        List<RiteCandidate> audible = await SelectAudibleAsync(candidates, 1, ct);

        return audible.Count > 0 ? audible[0] : null;
    }

    /// <summary>
    /// Walks the drawn ring candidates in order and returns up to <paramref name="needed"/> that can
    /// actually sound (SPEC §5.3, DECISIONS D25/D19). A band is audible when its cached
    /// <c>preview_url</c> is a streamable, allow-listed URL; when it has none and was never probed, the
    /// preview is resolved just-in-time (<see cref="PreviewResolver"/>), the result cached on the row,
    /// and later streamed through the existing proxy. A band that resolves to nothing — or to a host
    /// outside the proxy allow-list — is marked probed so a later ring does not re-resolve it every
    /// time (negative cache via the streaming-link marker; no <c>preview_url</c> is invented,
    /// Invariant 5). The artist rows are tracked; the cache writes are saved here so the work survives
    /// even a round that ends 204.
    /// </summary>
    private async Task<List<RiteCandidate>> SelectAudibleAsync(
        IReadOnlyList<RiteCandidate> candidates,
        int needed,
        CancellationToken ct)
    {
        List<Guid> ids = candidates.Select(c => c.ArtistId).ToList();

        Dictionary<Guid, Artist> artists = await _db.Artists
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        List<RiteCandidate> audible = new(needed);
        bool mutated = false;

        foreach (RiteCandidate candidate in candidates)
        {
            if (audible.Count >= needed)
            {
                break;
            }

            if (!artists.TryGetValue(candidate.ArtistId, out Artist? artist))
            {
                continue;
            }

            // Already audible: a cached, streamable preview URL.
            if (PreviewAudioProxy.IsAllowed(artist.PreviewUrl))
            {
                audible.Add(candidate);
                continue;
            }

            // Already probed and found inaudible: skip it without another network call (negative cache).
            if (WasProbed(artist.Links))
            {
                continue;
            }

            // Never probed and no usable cached URL: resolve online, iTunes first (DECISIONS D25).
            PreviewResolution? resolution = await _previews.ResolveAsync(artist.Name, artist.Links, ct);
            mutated = true;

            if (resolution is not null && PreviewAudioProxy.IsAllowed(resolution.Url))
            {
                artist.PreviewUrl = resolution.Url;
                MarkProbed(artist);
                audible.Add(candidate);

                _logger.LogInformation("Served band resolved a preview just-in-time from {Source}.", resolution.Source);
            }
            else
            {
                // Nothing streamable came back: cache the negative so the next ring skips it.
                MarkProbed(artist);
            }
        }

        if (mutated)
        {
            await _db.SaveChangesAsync(ct);
        }

        return audible;
    }

    /// <summary>
    /// True once an artist has been probed for a preview, whether or not one was found: it carries at
    /// least one curated <c>listen:</c> link (the same marker the ETL's preview pass leaves). A probed
    /// band with a null <c>preview_url</c> is genuinely inaudible and is not re-resolved every ring.
    /// </summary>
    private static bool WasProbed(IReadOnlyDictionary<string, string>? links)
    {
        return links is not null
            && links.Keys.Any(k => k.StartsWith(StreamingLinks.Prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Records that an artist was probed by merging the curated search links into <c>links</c> (the
    /// ETL's convention, reused here). This is the negative-cache marker AND supplies the reveal's
    /// outbound streaming links. A new dictionary instance makes the change detectable to EF; the raw
    /// MusicBrainz url-rels already in the column are preserved.
    /// </summary>
    private static void MarkProbed(Artist artist)
    {
        if (string.IsNullOrWhiteSpace(artist.Name))
        {
            return;
        }

        Dictionary<string, string> merged = artist.Links is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(artist.Links, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> link in StreamingLinks.Build(artist.Name, null, null))
        {
            merged[link.Key] = link.Value;
        }

        artist.Links = merged;
    }

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

    /// <summary>
    /// Records a versioned snapshot of the taste vector (feature C16, "your trajectory"). Called on
    /// every relevant change — cold-start seed and each summon — so the ordered snapshots trace the
    /// path the taste travelled. The vector is copied (a fresh <see cref="Vector"/>) so the snapshot
    /// does not share the live taste's array. Skipped when there is no vector to record.
    /// </summary>
    private void AddSnapshot(Guid userId, UserTaste taste)
    {
        if (taste.Embedding is null)
        {
            return;
        }

        _db.TasteSnapshots.Add(new TasteSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Embedding = new Vector(taste.Embedding.ToArray()),
            DepthScore = taste.DepthScore,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>
    /// Notifies any accepted friend the summoner just overtook in Depth Score (feature: "a friend
    /// surpassed you in rarity"). A friend F is crossed when the summoner's depth was at or below F's
    /// before this summon and strictly above it after — i.e. this summon is the moment they went
    /// deeper than F. Best-effort by contract: it never throws out of the summon path (a swallowed,
    /// logged failure is preferable to a failed summon) and does a single cheap pass over the
    /// summoner's accepted friends' live depth scores. Fires nothing when there are no friends or the
    /// summon crossed no one.
    /// </summary>
    private async Task NotifyRaritySurpassedAsync(Guid summonerId, int oldDepth, int newDepth, CancellationToken ct)
    {
        try
        {
            List<Guid> friendIds = await _db.Friendships
                .Where(f => f.Status == FriendshipStatus.Accepted
                    && (f.RequesterId == summonerId || f.AddresseeId == summonerId))
                .Select(f => f.RequesterId == summonerId ? f.AddresseeId : f.RequesterId)
                .ToListAsync(ct);

            if (friendIds.Count == 0)
            {
                return;
            }

            // Each friend's live Depth Score, computed the same way B15 does — from the ranks of what
            // they have summoned. A friend with no summons is absent here and reads as depth 0.
            Dictionary<Guid, int> friendDepths = (await _db.Rites
                    .Where(r => friendIds.Contains(r.UserId) && r.State == RiteState.Summoned)
                    .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => new { r.UserId, a.Rank })
                    .ToListAsync(ct))
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => DepthScore.Compute(g.Select(r => r.Rank)));

            foreach (Guid friendId in friendIds)
            {
                int friendDepth = friendDepths.TryGetValue(friendId, out int d) ? d : 0;

                // Crossed above: was at or below them, now strictly past them.
                if (oldDepth <= friendDepth && newDepth > friendDepth)
                {
                    await _notifications.CreateAsync(
                        friendId,
                        NotificationType.RaritySurpassed,
                        summonerId,
                        new { score = newDepth },
                        ct);
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort: a notification failure must never surface as a failed summon.
            _logger.LogWarning(ex, "Rarity-surpassed notification pass failed after a summon; the summon itself stands.");
        }
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
