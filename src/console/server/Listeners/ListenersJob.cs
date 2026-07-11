using Grimoire.Library.Data;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Listeners;

/// <summary>
/// Populates <c>artists.listeners</c> from Last.fm and derives <c>artists.rank</c> from it
/// (feature B15 / Ranks, SPEC section 6). The rank is what makes rarity inverse to popularity —
/// the whole point of the app — but it is null today because there was no Last.fm key; this pass
/// unblocks it. Candidates are the seeded bands (those with tags or releases), not the bare
/// member rows the edges pass added, which are people that Last.fm's band lookup would not
/// resolve anyway (D25). Lazy, batched and resumable: it processes only artists whose listeners
/// are still null, up to a per-run limit, so re-running continues where it left off. An artist
/// Last.fm cannot confidently match keeps <c>listeners = null</c> and therefore <c>rank = null</c>
/// — never invented. The source hides behind a feature flag: with no key it is disabled and this
/// job does nothing (Invariant 5 / D9).
/// </summary>
public sealed class ListenersJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnrichmentSource _lastFm;
    private readonly ListenersOptions _options;
    private readonly ILogger<ListenersJob> _logger;

    public ListenersJob(
        IServiceScopeFactory scopeFactory,
        IEnrichmentSource lastFm,
        ListenersOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<ListenersJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _lastFm = lastFm;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Listeners resolution";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_lastFm.Enabled)
        {
            _logger.LogWarning(
                "Last.fm source disabled: no API key configured. Listeners and ranks stay null (blocker Q5).");
            return;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        // Seeded bands only: tags or releases. Bare member rows (people) carry neither and would
        // not resolve against Last.fm's band lookup, so they are skipped clean (D25).
        List<Artist> candidates = await db.Artists
            .Where(a => a.Tags.Length > 0 || a.Releases.Any())
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        // Resume marker: a still-null listener count is "not yet resolved". Batching by limit
        // lets a run advance the corpus without one long sweep.
        List<Artist> pending = candidates
            .Where(a => a.Listeners is null)
            .Take(_options.Limit)
            .ToList();

        _logger.LogInformation("{Pending} artists pending listener resolution (of {Total} candidates).",
            pending.Count, candidates.Count);

        int resolved = 0;
        int attempted = 0;

        foreach (Artist artist in pending)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            ArtistEnrichment? enrichment = await _lastFm.FetchAsync(artist, ct);

            attempted++;

            if (enrichment?.Listeners is int listeners)
            {
                artist.Listeners = listeners;
                artist.Rank = RankCalculator.FromListeners(listeners);
                resolved++;

                await db.SaveChangesAsync(ct);
            }

            if (attempted % 25 == 0)
            {
                _logger.LogInformation("Attempted {Attempted}/{Total}, {Resolved} with a listener count.",
                    attempted, pending.Count, resolved);
            }
        }

        _logger.LogInformation(
            "Listeners batch complete: {Resolved}/{Attempted} resolved a listener count. Re-run to continue.",
            resolved, attempted);
    }
}
