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
/// CLAUDE.md). For every embedded artist it takes the cosine distance to all others and reads
/// off the 10th/50th/90th-percentile neighbour distance; it then reports the mean of each
/// across artists. The whole point: <b>if p10, p50 and p90 come out nearly equal, the D26 fix
/// did not take at this scale and the engine is still broken</b> — the ring search would select
/// the whole corpus at any slider position. The command prints the three numbers and a clear
/// verdict.
/// </summary>
public sealed class StatsJob : WorkerJob
{
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

        _logger.LogInformation("Computing neighbour-distance percentiles over {Count} centred embeddings...", vectors.Count);

        List<double> p10s = [];
        List<double> p50s = [];
        List<double> p90s = [];

        for (int i = 0; i < vectors.Count; i++)
        {
            List<double> neighbours = new(vectors.Count - 1);

            for (int j = 0; j < vectors.Count; j++)
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
        }

        NeighborStats.Spread mean = new(p10s.Average(), p50s.Average(), p90s.Average());
        double sep = mean.P90 - mean.P10;

        _logger.LogInformation("=== Neighbour-distance percentiles (mean over all artists) ===");
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
}
