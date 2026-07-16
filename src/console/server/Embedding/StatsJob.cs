using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Embedding;

/// <summary>
/// Measures whether the centred embeddings actually spread the catalogue (DECISIONS D26,
/// CLAUDE.md). It takes an artist's cosine distance to other artists and reads off the
/// 10th/50th/90th-percentile neighbour distance, then reports the mean of each across artists.
/// The whole point: <b>if p10, p50 and p90 come out nearly equal, the D26 fix did not take at
/// this scale and the engine is still broken</b> — the ring search would select the whole corpus
/// at any slider position. The command prints the three numbers and a clear verdict.
/// <para>
/// <b>Estimated from a sample at catalogue scale.</b> The exact figure is every artist against
/// every other: 176k² is 31 <em>billion</em> 768-dimension cosine distances, single-threaded —
/// it ran for 25 minutes pegged to one core without emitting a single line and was on course for
/// the better part of a day (MEMORY §6f). It is also unnecessary. What is being reported is the
/// <em>mean, over artists, of a percentile</em> — a statistic estimated perfectly well from a
/// sample, and one whose verdict (do the three diverge, or not?) turns on differences far larger
/// than the sampling error. Small corpora are still measured exactly, so the dev-scale numbers in
/// CLAUDE.md stay comparable. The sample is drawn with a fixed seed: a measurement you cannot
/// reproduce is not a measurement.
/// </para>
/// </summary>
public sealed class StatsJob : WorkerJob
{
    // Above this many embeddings, sample rather than sweep. Below it, an exact sweep is cheap
    // (2k² = 4M distances, a couple of seconds) and exactness costs nothing.
    private const int ExactUpTo = 2_000;

    // Probe artists to measure, and neighbours to measure each against. 1500 x 15000 = ~22M
    // distances: a minute, against a day. Both are unbiased — the percentiles of the distances to
    // a random 15k are an estimate of the percentiles against all of them, and the mean over 1500
    // probes is an estimate of the mean over all artists.
    private const int ProbeSample = 1_500;
    private const int NeighbourSample = 15_000;

    // Fixed so two runs of the same catalogue print the same numbers and a change in them means a
    // change in the catalogue, not a change in the dice.
    private const int SampleSeed = 26;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StatsJob> _logger;

    public StatsJob(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        ILogger<StatsJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override string CommandName => "Embedding stats";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        List<float[]> vectors = (await db.Artists
                .Where(a => a.Embedding != null)
                .Select(a => a.Embedding!)
                .ToListAsync(ct))
            .Select(v => v.ToArray())
            .ToList();

        if (vectors.Count < 3)
        {
            _logger.LogWarning("Only {Count} embeddings present; run the embeddings pass first.", vectors.Count);
            return;
        }

        bool exact = vectors.Count <= ExactUpTo;
        int probeCount = exact ? vectors.Count : Math.Min(ProbeSample, vectors.Count);
        int neighbourCount = exact ? vectors.Count : Math.Min(NeighbourSample, vectors.Count);

        if (exact)
        {
            _logger.LogInformation(
                "Computing neighbour-distance percentiles over {Count} centred embeddings (exact sweep)...",
                vectors.Count);
        }
        else
        {
            _logger.LogInformation(
                "Computing neighbour-distance percentiles over {Count} centred embeddings: estimating from "
                    + "{Probes} probes x {Neighbours} neighbours (seed {Seed}). An exact sweep is {Pairs:N0} "
                    + "distances and takes the better part of a day.",
                vectors.Count, probeCount, neighbourCount, SampleSeed, (long)vectors.Count * vectors.Count);
        }

        // One shuffle, two slices: probes measured against neighbours. Both drawn from the same
        // seeded permutation, so the run is reproducible.
        int[] order = Shuffled(vectors.Count, SampleSeed);
        int[] probes = order.Take(probeCount).ToArray();
        int[] neighbourIdx = exact ? order : order.Skip(probeCount % vectors.Count).Take(neighbourCount).ToArray();

        List<double> p10s = [];
        List<double> p50s = [];
        List<double> p90s = [];

        int done = 0;

        foreach (int i in probes)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            List<double> neighbours = new(neighbourIdx.Length);

            foreach (int j in neighbourIdx)
            {
                if (i == j)
                {
                    continue;
                }

                neighbours.Add(VectorMath.CosineDistance(vectors[i], vectors[j]));
            }

            NeighborStats.Spread spread = NeighborStats.SpreadOf(neighbours);
            p10s.Add(spread.P10);
            p50s.Add(spread.P50);
            p90s.Add(spread.P90);

            done++;

            // The old sweep printed nothing until it finished, so a run that would take hours was
            // indistinguishable from a run that had hung. Say something.
            if (done % 100 == 0)
            {
                _logger.LogInformation("Probed {Done}/{Total}...", done, probes.Length);
            }
        }

        if (p10s.Count == 0)
        {
            _logger.LogWarning("Cancelled before any probe completed; no verdict.");
            return;
        }

        NeighborStats.Spread mean = new(p10s.Average(), p50s.Average(), p90s.Average());
        double sep = mean.P90 - mean.P10;

        _logger.LogInformation(
            exact
                ? "=== Neighbour-distance percentiles (mean over all {Probes} artists, exact) ==="
                : "=== Neighbour-distance percentiles (estimated: {Probes} probes x {Neighbours} neighbours) ===",
            probes.Length, neighbourIdx.Length);
        _logger.LogInformation("  p10 (near neighbour): {P10:F4}", mean.P10);
        _logger.LogInformation("  p50 (median)        : {P50:F4}", mean.P50);
        _logger.LogInformation("  p90 (far neighbour) : {P90:F4}", mean.P90);
        _logger.LogInformation("  spread p10->p90     : {Sep:F4}", sep);

        if (mean.IsDegenerate())
        {
            _logger.LogError(
                "VERDICT: DEGENERATE — p10, p50 and p90 are nearly equal (spread {Sep:F4}). The D26 centring did not "
                + "take at this scale; the ring search is broken and the Comfort<->Abyss slider would move nothing.",
                sep);
        }
        else
        {
            _logger.LogInformation(
                "VERDICT: HEALTHY — the three percentiles diverge (spread {Sep:F4}); the slider has room to travel.",
                sep);
        }
    }

    /// <summary>A seeded Fisher-Yates permutation of 0..count-1 — the same sample on every run.</summary>
    private static int[] Shuffled(int count, int seed)
    {
        int[] order = new int[count];

        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        Random rng = new(seed);

        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }
}
