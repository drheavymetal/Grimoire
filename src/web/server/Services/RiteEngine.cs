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
    /// Fraction of the corpus nearest the repulsion centroid to push out of the pool (DECISIONS D4).
    /// </summary>
    public double RepulsionNearPct { get; set; } = 0.20;
}

/// <summary>Hard filters for the pool (feature C13): decade and country only.</summary>
public sealed record RiteFilters(string? Country, int? DecadeFrom, int? DecadeTo);

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
/// The pool is <b>the servable set</b>: <c>preview_url IS NOT NULL</c> (DECISIONS D25 — the Rite
/// pool is what can actually sound, a design constant not an edge case) and
/// <c>embedding IS NOT NULL</c>. It also excludes what the user has already judged, except a
/// banished band older than six months, which returns (feature C3, second chance). Repulsion
/// actively subtracts: anything too close to the banished centroid is dropped (D4).
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

    public RiteEngine(GrimoireDbContext db, RiteEngineOptions options)
    {
        _db = db;
        _options = options;
    }

    /// <summary>
    /// Finds one band to serve, or null when the ring is empty (a legitimate outcome for a tight
    /// slider on a small pool — the caller degrades with a designed empty state, not an error).
    /// </summary>
    public async Task<RiteCandidate?> FindAsync(
        Guid userId,
        Vector taste,
        Vector? repulsion,
        double comfort,
        RiteFilters filters,
        CancellationToken ct)
    {
        // 1. Sample the servable pool's distance distribution to the taste vector, then read the
        //    two ring radii at the slider's percentiles. The sample defines the ring; the query
        //    below applies it.
        List<double> sample = await ServablePool()
            .OrderBy(_ => EF.Functions.Random())
            .Take(_options.SampleSize)
            .Select(a => a.Embedding!.CosineDistance(taste))
            .ToListAsync(ct);

        if (sample.Count == 0)
        {
            return null;
        }

        (double rLo, double rHi) = RingResolver.ResolveRadii(comfort, sample, _options.RingWidthPct);
        (double loPct, double hiPct) = RingResolver.Percentiles(comfort, _options.RingWidthPct);
        double riskPercentile = (loPct + hiPct) / 2.0;

        // 2. If the user has banished anything, compute the safe radius around the repulsion
        //    centroid so we can push the pool away from it (DECISIONS D4).
        double? safeRadius = null;
        if (repulsion is not null)
        {
            List<double> repulsionSample = await ServablePool()
                .OrderBy(_ => EF.Functions.Random())
                .Take(_options.SampleSize)
                .Select(a => a.Embedding!.CosineDistance(repulsion))
                .ToListAsync(ct);

            if (repulsionSample.Count > 0)
            {
                safeRadius = RingResolver.SafeRadius(repulsionSample, _options.RepulsionNearPct);
            }
        }

        // 3. The ranged HNSW query. Exclude what the user already judged (except banished bands
        //    past their second-chance window), apply the decade/country filters, keep the ring,
        //    subtract the repulsion, and take one band at random from what survives.
        DateTimeOffset secondChanceCutoff = DateTimeOffset.UtcNow - SecondChanceAfter;

        IQueryable<Guid> excluded = _db.Rites
            .Where(r => r.UserId == userId)
            .Where(r => !(r.State == Library.Models.RiteState.Banished
                          && r.ResolvedAt != null
                          && r.ResolvedAt < secondChanceCutoff))
            .Select(r => r.ArtistId);

        var ranked = ServablePool()
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
                Distance = a.Embedding!.CosineDistance(taste),
                RepulsionDistance = repulsion == null ? (double?)null : a.Embedding!.CosineDistance(repulsion),
            })
            .Where(x => x.Distance >= rLo && x.Distance <= rHi);

        if (safeRadius is double safe)
        {
            inRing = inRing.Where(x => x.RepulsionDistance != null && x.RepulsionDistance > safe);
        }

        var chosen = await inRing
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(ct);

        if (chosen is null)
        {
            return null;
        }

        return new RiteCandidate(chosen.Id, chosen.Distance, riskPercentile);
    }

    /// <summary>The servable pool: embeddable and audible (DECISIONS D25).</summary>
    private IQueryable<Library.Models.Artist> ServablePool()
    {
        return _db.Artists.Where(a => a.Embedding != null && a.PreviewUrl != null);
    }
}
