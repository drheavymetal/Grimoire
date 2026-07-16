using Grimoire.Library.Data;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikipedia;

/// <summary>
/// Fills <c>artists.abstract</c> (and <c>artists.abstract_url</c>, for CC BY-SA attribution) from
/// English Wikipedia, matched accurately through the MusicBrainz id → Wikidata (<c>wdt:P434</c>) →
/// Wikipedia bridge (never by name — homonyms are the trap). Most bands have no biography today;
/// this pass closes that gap for the ones that have an article.
/// <para>
/// Scope: artists that carry a MusicBrainz id and are not yet checked, worked most-popular-first
/// (Known bands actually have articles; the underground mostly will not match, so the ones that
/// pay off go first). Resumable and batched on the <see cref="Artist.AbstractCheckedAt"/> marker: a
/// checked artist, matched or not, is stamped so a re-run never re-queries a miss and simply
/// continues where it left off.
/// </para>
/// <para>
/// Note: filling <c>Abstract</c> changes the text the embedding pass builds its vector from (the
/// abstract is part of that text), so matched artists should be <b>re-embedded</b> later. This job
/// deliberately does <b>not</b> trigger re-embedding — that is a separate, explicit pass.
/// </para>
/// </summary>
public sealed class WikipediaJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikipediaSource _source;
    private readonly WikipediaOptions _options;
    private readonly ILogger<WikipediaJob> _logger;

    public WikipediaJob(
        IServiceScopeFactory scopeFactory,
        WikipediaSource source,
        WikipediaOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<WikipediaJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _source = source;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Wikipedia biography resolution";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        // No biography yet, never checked, and carries a MusicBrainz id (the only accurate match).
        // Ordered by listeners so the bands people actually meet get their biography first; the
        // underground (mostly unmatched) sorts last but is still attempted once, then stamped.
        List<Artist> pending = await db.Artists
            .Where(a => a.Abstract == null
                && a.AbstractCheckedAt == null
                && a.Mbid != Guid.Empty)
            .OrderByDescending(a => a.Listeners ?? -1)
            .ThenBy(a => a.Name)
            .Take(_options.Limit)
            .ToListAsync(ct);

        _logger.LogInformation("{Pending} artists pending Wikipedia biography resolution.", pending.Count);

        int matched = 0;
        int attempted = 0;
        int unavailable = 0;

        // Chunk into one WDQS query per BatchSize MBIDs — the throughput win over one query per artist.
        foreach (Artist[] batch in pending.Chunk(_options.BatchSize))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            IReadOnlyDictionary<Guid, BiographyResult> results = await _source.ResolveBatchAsync(batch, ct);

            foreach (Artist artist in batch)
            {
                if (!results.TryGetValue(artist.Mbid, out BiographyResult result))
                {
                    continue;
                }

                switch (result.Outcome)
                {
                    case EnrichmentOutcome.Matched:
                        // A definitive hit: store the biography and stamp it checked.
                        artist.Abstract = result.Biography!.Abstract;
                        artist.AbstractUrl = result.Biography.Url;
                        artist.AbstractCheckedAt = DateTime.UtcNow;
                        matched++;
                        attempted++;
                        break;

                    case EnrichmentOutcome.NoData:
                        // A definitive miss: stamp so a re-run never fetches it again.
                        artist.AbstractCheckedAt = DateTime.UtcNow;
                        attempted++;
                        break;

                    case EnrichmentOutcome.Unavailable:
                        // A transient WDQS/Wikipedia failure: leave UNSTAMPED so a later run retries.
                        // Stamping here would record a timeout forever as "this band has no biography".
                        unavailable++;
                        break;
                }
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Attempted {Attempted}/{Total}, {Matched} matched, {Unavailable} left for retry.",
                attempted, pending.Count, matched, unavailable);
        }

        _logger.LogInformation(
            "Wikipedia batch complete: {Matched}/{Attempted} matched, {Unavailable} deferred (transient). Re-run to continue.",
            matched, attempted, unavailable);
    }
}
