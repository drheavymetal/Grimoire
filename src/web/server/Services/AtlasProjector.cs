using Grimoire.Library.Data;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>
/// Places a live taste vector on the Atlas map. Loads the stored (embedding, xy) pairs once,
/// reconstructs the offline PCA basis (<see cref="AtlasProjection"/>) and caches it for the process
/// lifetime — the projection only changes when the offline <c>atlas</c> pass re-runs, which is rare.
/// Registered as a singleton; it reaches for a scoped <see cref="GrimoireDbContext"/> through the
/// scope factory only on the first projection.
/// </summary>
public sealed class AtlasProjector
{
    // Enough (embedding, xy) pairs to recover the offline PCA map by least squares; far below the full
    // ~175k, so the reconstruction stays cheap in time and memory. See GetBasisAsync.
    private const int BasisSampleSize = 12000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AtlasProjection.Basis? _basis;
    // Volatile so the lock-free fast path in GetBasisAsync is safe under weak memory models (e.g.
    // ARM64): the release write of _loaded (after _basis is set) publishes _basis, and the acquire
    // read here sees it — no reader can observe _loaded == true with _basis still null.
    private volatile bool _loaded;

    public AtlasProjector(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Projects a taste vector onto the Atlas plane, or null when the basis cannot be built (too few
    /// projected stars, or a degenerate projection) — the caller then omits the "you are here"
    /// marker rather than inventing a position.
    /// </summary>
    public async Task<(double X, double Y)?> ProjectTasteAsync(float[] taste, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taste);

        AtlasProjection.Basis? basis = await GetBasisAsync(ct);

        if (basis is null || taste.Length != basis.Mean.Length)
        {
            return null;
        }

        return AtlasProjection.Project(basis, taste);
    }

    private async Task<AtlasProjection.Basis?> GetBasisAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            return _basis;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_loaded)
            {
                return _basis;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

            // Reconstruct the linear PCA basis from a bounded, stable sample rather than all ~175k
            // (embedding, xy) pairs: loading every 768-float embedding was ~½ GB into the API on the
            // first projection, enough to OOM a shared box. Least-squares recovers the same offline
            // map from a representative sample, and OrderBy(Id) over random Guids is a deterministic
            // sample, so the basis — and the "you are here" marker — is stable across restarts.
            var rows = await db.Artists
                .AsNoTracking()
                .Where(a => a.Embedding != null && a.XyX != null && a.XyY != null)
                .OrderBy(a => a.Id)
                .Take(BasisSampleSize)
                .Select(a => new { a.Embedding, a.XyX, a.XyY })
                .ToListAsync(ct);

            List<float[]> embeddings = rows.Select(r => r.Embedding!.ToArray()).ToList();
            List<double> xs = rows.Select(r => r.XyX!.Value).ToList();
            List<double> ys = rows.Select(r => r.XyY!.Value).ToList();

            _basis = AtlasProjection.Reconstruct(embeddings, xs, ys);
            _loaded = true;
            return _basis;
        }
        finally
        {
            _gate.Release();
        }
    }
}
