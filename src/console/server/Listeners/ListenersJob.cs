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
        //
        // Ordered by discography size, largest first. A complete sweep attempts every candidate so
        // the order does not change coverage — but real bands (many releases) resolve on Last.fm far
        // more often than the alphabetical junk drawer (single-release keyboard-mash names), so this
        // front-loads the value and, if the run is ever interrupted, the bands the Rite most needs a
        // rank for are already done. Ties broken by name for a stable, resumable sequence.
        List<Artist> candidates = await db.Artists
            .Where(a => a.Tags.Length > 0 || a.Releases.Any())
            .OrderByDescending(a => a.Releases.Count())
            .ThenBy(a => a.Name)
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
        int tagged = 0;
        int attempted = 0;

        foreach (Artist artist in pending)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            ArtistEnrichment? enrichment = await _lastFm.FetchAsync(artist, ct);

            attempted++;

            bool changed = false;

            if (enrichment?.Listeners is int listeners)
            {
                artist.Listeners = listeners;
                artist.Rank = RankCalculator.FromListeners(listeners);
                resolved++;
                changed = true;
            }

            // Backfill genre tags only where the artist has none, so Last.fm never overwrites the
            // cleaner MusicBrainz tags and only newly-tagged bands need re-embedding afterwards
            // (MEMORY §6b). Note: an artist already carrying a listener count is not in this pass's
            // pending set, so a tags-only backfill for the earlier A/B batch is a separate concern.
            if (artist.Tags.Length == 0 && enrichment is { Tags.Count: > 0 })
            {
                artist.Tags = [.. enrichment.Tags];
                tagged++;
                changed = true;
            }

            if (changed)
            {
                await db.SaveChangesAsync(ct);
            }

            if (attempted % 25 == 0)
            {
                _logger.LogInformation(
                    "Attempted {Attempted}/{Total}, {Resolved} with a listener count, {Tagged} newly tagged.",
                    attempted, pending.Count, resolved, tagged);
            }
        }

        _logger.LogInformation(
            "Listeners batch complete: {Resolved}/{Attempted} resolved a listener count, {Tagged} gained tags. Re-run to continue.",
            resolved, attempted, tagged);
    }
}
