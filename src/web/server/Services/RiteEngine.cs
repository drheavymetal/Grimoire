using Grimoire.Library.Data;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>Tunables for the ring search, bound from the "Rite" configuration section.</summary>
public sealed class RiteEngineOptions
{
    /// <summary>How many servable artists to sample when estimating the distance distribution.</summary>
    public int SampleSize { get; set; } = 2000;

    /// <summary>Width of the percentile window the Comfort ↔ Abyss slider slides (DECISIONS D26).</summary>
    public double RingWidthPct { get; set; } = 0.20;

    /// <summary>
    /// Exponent bending the slider's travel toward the low percentiles (DECISIONS D68). See
    /// <see cref="RingResolver.DefaultReachCurve"/> — 1.0 restores the linear map whose midpoint
    /// measurably served random bands.
    /// </summary>
    public double ReachCurve { get; set; } = RingResolver.DefaultReachCurve;

    /// <summary>
    /// Fraction of the corpus nearest the repulsion centroid to push out of the pool (DECISIONS D4).
    /// </summary>
    public double RepulsionNearPct { get; set; } = 0.20;

    /// <summary>
    /// Weight of the rarity term in the within-ring ordering (SPEC §6, superseding D31's "no rarity
    /// term while listeners is null"). Higher biases harder toward rare bands; 0 disables it and the
    /// pick is uniform-within-ring. See <see cref="RaritySelector"/>.
    /// </summary>
    public double RarityWeight { get; set; } = RaritySelector.DefaultRarityWeight;
}

/// <summary>
/// Hard filters for the pool (feature C13, plus the tag/theme lanes added 2026-07-15). Decade and
/// country narrow the ring; the three needles narrow it further, ANDed together when more than one
/// is present. The tasting stays blind either way — a lane only decides which bands the ring may
/// draw from, never reveals one.
///
/// <para>
/// <see cref="TagNeedle"/> is a lower-case tag substring (either a raw clicked tag or one resolved
/// from a <see cref="RiteGenres"/> key by the controller). <see cref="ThemeNeedle"/> plus
/// <see cref="ThemeKind"/> scope by lyrical theme: <c>"lyrical"</c> matches Metal Archives'
/// curated <c>lyrical_themes</c> against the needle; <c>"mined"</c> matches the needle as a
/// <see cref="TitleLexicon"/> theme id and keeps bands with a recording title evoking any of that
/// theme's keywords. All three default to null, which leaves the rite fully open.
/// </para>
/// </summary>
public sealed record RiteFilters(
    string? Country,
    int? DecadeFrom,
    int? DecadeTo,
    string? TagNeedle = null,
    string? ThemeNeedle = null,
    string? ThemeKind = null);

/// <summary>A candidate the engine chose from the ring, with its distance and slider risk.</summary>
public sealed record RiteCandidate(Guid ArtistId, double Distance, double RiskPercentile);

/// <summary>
/// The discovery engine (SPEC §6, DECISIONS D4 corrected by D26). It searches in a <b>ring</b>,
/// not a ball: not "more of the same", but the farthest bands that still fall inside your
/// tolerance. The ring is expressed in <b>percentiles</b>, because every nomic-embed-text
/// distance lives in a thin shell and an absolute radius would select the whole corpus.
///
/// <para>
/// The algorithm: sample a few thousand servable artists, measure their cosine distance to the
/// user's taste, read off the two radii at the slider's percentiles (<see cref="RingResolver"/>),
/// then hand those radii to a ranged HNSW query. Percentiles toward the user, radii toward the
/// index — no <c>ORDER BY</c> over the whole catalogue.
/// </para>
///
/// <para>
/// The pool is <b>the embedded catalogue</b>: <c>embedding IS NOT NULL</c>. It no longer requires
/// <c>preview_url IS NOT NULL</c>: at 207k artists the Rite cannot pre-resolve a preview for all of
/// them under the iTunes ceiling (DECISIONS D25/D19), so audibility is decided at serve time —
/// <see cref="RiteController"/> resolves the preview just-in-time for the band it is about to serve
/// and skips to the next candidate when a band turns out to be inaudible. Pre-filtering on
/// <c>preview_url</c> here would strand the ring in the tiny already-resolved pool. It still excludes
/// what the user has already judged, except a banished band older than six months, which returns
/// (feature C3, second chance). Repulsion actively subtracts: anything too close to the banished
/// centroid is dropped (D4).
/// </para>
///
/// <para>
/// The taste and repulsion vectors handed in are ALREADY centred (DECISIONS D26): they were
/// built by averaging stored centred embeddings, and the indexed embeddings are centred too, so
/// the distances are directly comparable. Nothing here re-centres — see <see cref="TasteMath"/>.
/// </para>
/// </summary>
public sealed class RiteEngine
{
    /// <summary>A banished band is eligible again after this long (feature C3).</summary>
    public static readonly TimeSpan SecondChanceAfter = TimeSpan.FromDays(182);

    private readonly GrimoireDbContext _db;
    private readonly RiteEngineOptions _options;
    private readonly Func<double> _nextUnit;

    public RiteEngine(GrimoireDbContext db, RiteEngineOptions options)
    {
        _db = db;
        _options = options;
        _nextUnit = DefaultNextUnit;
    }

    /// <summary>A uniform draw strictly inside (0, 1), safe for the Gumbel transform in the pick.</summary>
    private static double DefaultNextUnit()
    {
        double u = Random.Shared.NextDouble();
        return u <= 0.0 ? double.Epsilon : u;
    }

    /// <summary>
    /// Finds one band to serve, or null when the ring is empty (a legitimate outcome for a tight
    /// slider on a small pool — the caller degrades with a designed empty state, not an error).
    /// When <paramref name="scorableOnly"/> is set the pool is further narrowed to bands with a
    /// formed year, a country and at least one tag, so the "guess the decade" game (feature C27)
    /// can score every dimension against a real value.
    /// </summary>
    public async Task<RiteCandidate?> FindAsync(
        Guid userId,
        Vector taste,
        Vector? repulsion,
        double comfort,
        RiteFilters filters,
        CancellationToken ct,
        bool scorableOnly = false)
    {
        IReadOnlyList<RiteCandidate> chosen = await FindManyAsync(
            userId, taste, repulsion, comfort, filters, 1, ct, scorableOnly);

        return chosen.Count > 0 ? chosen[0] : null;
    }

    /// <summary>
    /// Finds up to <paramref name="count"/> <b>distinct</b> bands from the same ring, for the blind
    /// duel (feature C2): two bands the user picks between. Returns fewer than asked (possibly zero)
    /// when the ring cannot supply that many — the caller shows a designed empty state, never an
    /// error. Each band is drawn without replacement by the same rarity-weighted pick that
    /// <see cref="FindAsync"/> uses, so a duel is two independent, rarity-biased draws from the ring.
    /// </summary>
    public async Task<IReadOnlyList<RiteCandidate>> FindManyAsync(
        Guid userId,
        Vector taste,
        Vector? repulsion,
        double comfort,
        RiteFilters filters,
        int count,
        CancellationToken ct,
        bool scorableOnly = false)
    {
        if (count <= 0)
        {
            return [];
        }

        (List<RingRow> ring, double riskPercentile) = await RingAsync(
            userId, taste, repulsion, comfort, filters, scorableOnly, ct);

        if (ring.Count == 0)
        {
            return [];
        }

        // Draw `count` distinct bands without replacement, each by the rarity-weighted pick. The
        // ring already fixed the distance band (D26/D31); the rarity term only reorders inside it,
        // biasing toward rarity while keeping the exploration, and null listeners weigh NEUTRALLY so
        // the dark tail without Last.fm data never dominates (see RaritySelector).
        List<RingRow> pool = ring;
        List<RiteCandidate> chosen = new(Math.Min(count, pool.Count));

        while (chosen.Count < count && pool.Count > 0)
        {
            double[] rarityTerms = pool
                .Select(c => RaritySelector.RarityTerm(c.Listeners, _options.RarityWeight))
                .ToArray();

            int index = RaritySelector.SelectIndex(rarityTerms, _nextUnit);
            RingRow row = pool[index];
            chosen.Add(new RiteCandidate(row.Id, row.Distance, riskPercentile));
            pool.RemoveAt(index);
        }

        return chosen;
    }

    /// <summary>A band that survived the ring query, with what the rarity pick needs.</summary>
    private sealed record RingRow(Guid Id, double Distance, int? Listeners);

    /// <summary>
    /// The shared ring query behind both <see cref="FindAsync"/> and <see cref="FindManyAsync"/>:
    /// sample the distance distribution, read the two radii at the slider's percentiles, then run
    /// the ranged HNSW query — excluding what the user already judged (except banished bands past
    /// their second-chance window), applying the decade/country filters, keeping the ring and
    /// subtracting the repulsion. Returns the survivors and the risk percentile the pick reports.
    /// </summary>
    private async Task<(List<RingRow> Ring, double RiskPercentile)> RingAsync(
        Guid userId,
        Vector taste,
        Vector? repulsion,
        double comfort,
        RiteFilters filters,
        bool scorableOnly,
        CancellationToken ct)
    {
        // 1. Sample the servable pool's distance distribution to the taste vector, then read the
        //    two ring radii at the slider's percentiles. The sample defines the ring; the query
        //    below applies it. The sample is drawn from the SAME pool the query uses, so scorable
        //    duels/decade games get percentiles calibrated to the scorable pool.
        List<double> sample = await ServablePool(scorableOnly, filters)
            .OrderBy(_ => EF.Functions.Random())
            .Take(_options.SampleSize)
            .Select(a => a.Embedding!.CosineDistance(taste))
            .ToListAsync(ct);

        if (sample.Count == 0)
        {
            return ([], (Percentile(comfort).Lo + Percentile(comfort).Hi) / 2.0);
        }

        (double rLo, double rHi) = RingResolver.ResolveRadii(comfort, sample, _options.RingWidthPct, _options.ReachCurve);
        (double loPct, double hiPct) = RingResolver.Percentiles(comfort, _options.RingWidthPct, _options.ReachCurve);
        double riskPercentile = (loPct + hiPct) / 2.0;

        // 2. If the user has banished anything, compute the safe radius around the repulsion
        //    centroid so we can push the pool away from it (DECISIONS D4).
        double? safeRadius = null;
        if (repulsion is not null)
        {
            List<double> repulsionSample = await ServablePool(scorableOnly, filters)
                .OrderBy(_ => EF.Functions.Random())
                .Take(_options.SampleSize)
                .Select(a => a.Embedding!.CosineDistance(repulsion))
                .ToListAsync(ct);

            if (repulsionSample.Count > 0)
            {
                safeRadius = RingResolver.SafeRadius(repulsionSample, _options.RepulsionNearPct);
            }
        }

        // 3. The ranged HNSW query.
        DateTimeOffset secondChanceCutoff = DateTimeOffset.UtcNow - SecondChanceAfter;

        IQueryable<Guid> excluded = _db.Rites
            .Where(r => r.UserId == userId)
            .Where(r => !(r.State == Library.Models.RiteState.Banished
                          && r.ResolvedAt != null
                          && r.ResolvedAt < secondChanceCutoff))
            .Select(r => r.ArtistId);

        var ranked = ServablePool(scorableOnly, filters)
            .Where(a => !excluded.Contains(a.Id));

        if (!string.IsNullOrWhiteSpace(filters.Country))
        {
            string country = filters.Country.Trim();
            ranked = ranked.Where(a => a.Country == country);
        }

        if (filters.DecadeFrom is int from)
        {
            ranked = ranked.Where(a => a.FormedYear != null && a.FormedYear >= from);
        }

        if (filters.DecadeTo is int to)
        {
            ranked = ranked.Where(a => a.FormedYear != null && a.FormedYear <= to);
        }

        var inRing = ranked
            .Select(a => new
            {
                a.Id,
                a.Listeners,
                Distance = a.Embedding!.CosineDistance(taste),
                RepulsionDistance = repulsion == null ? (double?)null : a.Embedding!.CosineDistance(repulsion),
            })
            .Where(x => x.Distance >= rLo && x.Distance <= rHi);

        if (safeRadius is double safe)
        {
            inRing = inRing.Where(x => x.RepulsionDistance != null && x.RepulsionDistance > safe);
        }

        List<RingRow> ring = await inRing
            .Select(x => new RingRow(x.Id, x.Distance, x.Listeners))
            .ToListAsync(ct);

        return (ring, riskPercentile);
    }

    // The band for a comfort value, honouring the configured window and curve. Reads the options
    // rather than RingResolver's defaults: the empty-pool fallback must report the same risk the
    // real path would have reported, or the number shown to the user drifts from the engine's.
    private (double Lo, double Hi) Percentile(double comfort)
    {
        return RingResolver.Percentiles(comfort, _options.RingWidthPct, _options.ReachCurve);
    }

    /// <summary>
    /// The ring pool: what may be discovered at all (<see cref="DiscoverableArtists"/> — an embedding
    /// AND a discography; see there for the session drummers that used to be served as bands).
    ///
    /// <para>
    /// Audibility is NOT filtered here — at 207k artists a preview cannot be pre-resolved for all of them
    /// (DECISIONS D25/D19), so the controller resolves it just-in-time at serve time and skips inaudible
    /// bands. When <paramref name="scorableOnly"/> is set the pool is narrowed further to bands with a
    /// formed year, a country and at least one tag, so the decade game (feature C27) never serves a band
    /// it cannot score.
    /// </para>
    /// </summary>
    private IQueryable<Library.Models.Artist> ServablePool(bool scorableOnly = false, RiteFilters? filters = null)
    {
        IQueryable<Library.Models.Artist> pool = _db.Artists.Discoverable();

        if (scorableOnly)
        {
            pool = pool.Where(a => a.FormedYear != null && a.Country != null && a.Tags.Length > 0);
        }

        string? tagNeedle = SearchNeedle.Clean(filters?.TagNeedle);
        if (tagNeedle is not null)
        {
            // ILIKE substring over the band's tags so a family catches its compounds ("black metal"
            // also matches "atmospheric black metal"). Applied to BOTH the distance sample and the
            // ring query, so the ring radii are calibrated to the lane's own distribution, not the
            // whole corpus. Npgsql translates Array.Any(ILike) via unnest.
            string pattern = $"%{tagNeedle}%";
            pool = pool.Where(a => a.Tags.Any(t => EF.Functions.ILike(t, pattern)));
        }

        string? themeNeedle = SearchNeedle.Clean(filters?.ThemeNeedle);
        string? themeKind = filters?.ThemeKind?.Trim().ToLowerInvariant();

        if (themeNeedle is not null && themeKind == "lyrical")
        {
            // Metal Archives' curated lyrical themes (D48): match the needle against the band's own
            // theme strings. A blind pool filter, never a reveal.
            string pattern = $"%{themeNeedle}%";
            pool = pool.Where(a => a.LyricalThemes.Any(x => EF.Functions.ILike(x, pattern)));
        }
        else if (themeNeedle is not null && themeKind == "mined")
        {
            // Song-title mining (C21): the needle is a TitleLexicon theme id; keep bands with at least
            // one recording title evoking any of that theme's keywords (EXISTS over recordings, using
            // the recordings-title trigram index). An unknown theme has no keywords, so the pool
            // narrows to nothing rather than silently widening to the whole corpus.
            IReadOnlyList<string> keywords = TitleLexicon.KeywordsFor(themeNeedle);

            if (keywords.Count == 0)
            {
                pool = pool.Where(a => false);
            }
            else
            {
                string[] patterns = keywords.Select(k => $"%{k}%").ToArray();
                pool = pool.Where(a => _db.Recordings.Any(r =>
                    r.Release!.ArtistId == a.Id && patterns.Any(p => EF.Functions.ILike(r.Title, p))));
            }
        }

        return pool;
    }
}
