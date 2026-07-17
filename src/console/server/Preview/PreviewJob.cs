using Grimoire.Library.Data;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Preview;

/// <summary>
/// Resolves audio previews and curated streaming links (features B13/B26, DECISIONS D10/D25).
/// iTunes is asked first and Deezer only complements it — never the other way round. The pass
/// is lazy and batched, never one big sweep: it processes only artists not yet attempted
/// (those with no <c>listen:</c> link), up to a per-run limit, so re-running resumes where it
/// left off. An artist with no match keeps <c>preview_url = null</c> — a real gap, since about
/// half the underground is genuinely inaudible.
///
/// <para>
/// It runs in two phases, and the second exists because of what the first cannot reach.
/// <b>Resolve</b> asks about bands nobody has ever asked about, and now keeps every clip the answer
/// contained instead of one (DECISIONS D67) — no extra request, the alternates were always in the
/// response. <b>Harvest</b> collects those alternates for bands whose <c>preview_url</c> was resolved
/// before any of this existed, or resolved just-in-time by The Rite at serve time (D40), which is how
/// nearly every audible band in production got its audio. Those bands are already marked probed, so the
/// first phase can never see them again and their alternates would be unreachable for ever. Harvest
/// costs one request per band, once — the only requests this change adds, and only for bands somebody
/// actually discovered.
/// </para>
/// </summary>
public sealed class PreviewJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<IEnrichmentSource> _sources;
    private readonly PreviewOptions _options;
    private readonly ILogger<PreviewJob> _logger;

    public PreviewJob(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IEnrichmentSource> sources,
        PreviewOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<PreviewJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _sources = sources.ToList();
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Preview resolution";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        IEnrichmentSource? itunes = _sources.FirstOrDefault(s => s.Name == "iTunes" && s.Enabled);
        IEnrichmentSource? deezer = _sources.FirstOrDefault(s => s.Name == "Deezer" && s.Enabled);

        _logger.LogInformation(
            "Preview sources — iTunes: {ITunes}, Deezer: {Deezer}. Batch limit: {Limit}.",
            itunes is not null ? "on" : "off", deezer is not null ? "on" : "off", _options.Limit);

        if (itunes is null && deezer is null)
        {
            // Every source is flagged off, so nothing will be asked — and a pass that asks nothing must
            // record nothing. Without this the flags become a way to poison the catalogue: a source that
            // is off is skipped and counted as a definitive "nothing here" (D9), so with all of them off
            // every band would be stamped as harvested, and as having no audio, without a single request
            // leaving the machine. Markers make that permanent and invisible — the D61 failure, reached
            // through configuration rather than through a 429.
            _logger.LogWarning("Every preview source is disabled: nothing to ask, so nothing is recorded.");
            return;
        }

        await ResolveAsync(db, itunes, deezer, ct);
        await HarvestAsync(db, itunes, deezer, ct);
    }

    /// <summary>
    /// Phase one, unchanged in what it means: bands nobody has asked about yet get a
    /// <c>preview_url</c>, their curated links, and — new, and for free — every other clip the same
    /// answers already carried.
    /// </summary>
    private async Task ResolveAsync(
        GrimoireDbContext db,
        IEnrichmentSource? itunes,
        IEnrichmentSource? deezer,
        CancellationToken ct)
    {
        // Candidates are the seeded corpus (tags or releases), not the bare member rows the
        // edges pass added. "Attempted" = already carries curated listen: links, so those are
        // excluded and the batch always advances.
        //
        // The probed marker is a key inside a jsonb column, which this stack cannot filter in SQL, so
        // the candidate set genuinely has to come back and be sieved here. What must NOT come back with
        // it is the rest of the row. Every artist carries a 768-dimension embedding: measured against
        // production's catalogue, the unprojected query is 389 MB of payload — and several times that
        // once EF has materialised and is tracking it — to end up choosing sixty rows. The projection
        // measures 35 MB and, more importantly, keeps the vectors out of the change tracker entirely.
        // Only the chosen batch is then loaded as tracked entities. Same selection, same order, same
        // limit; this pass was written when the catalogue was 2 500 artists and never revisited (§6f).
        var candidates = await db.Artists
            .Where(a => a.Tags.Length > 0 || a.Releases.Any())
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Links })
            .ToListAsync(ct);

        List<Guid> pendingIds = candidates
            .Where(a => a.Links is null || !a.Links.Keys.Any(k => k.StartsWith(StreamingLinks.Prefix, StringComparison.Ordinal)))
            .Take(_options.Limit)
            .Select(a => a.Id)
            .ToList();

        List<Artist> pending = await db.Artists
            .Where(a => pendingIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        _logger.LogInformation("{Pending} artists pending preview resolution (of {Total} candidates).",
            pending.Count, candidates.Count);

        int withPreview = 0;
        int attempted = 0;
        int unavailable = 0;
        int clips = 0;

        foreach (Artist artist in pending)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            Lookup lookup = await AskAsync(artist, itunes, deezer, ct);

            // Neither source could answer (429s, a network blip): we learned nothing about this
            // artist, so write nothing. Recording "no preview" here would be recording the outage.
            if (lookup.NobodyAnswered)
            {
                unavailable++;
                continue;
            }

            ArtistEnrichment? apple = lookup.Apple.Enrichment;
            ArtistEnrichment? dz = lookup.Deezer.Enrichment;

            // iTunes first, Deezer as complement (D25) — never the reverse.
            string? previewUrl = apple?.PreviewUrl ?? dz?.PreviewUrl;

            string? appleLink = apple is not null && apple.Links.TryGetValue(StreamingLinks.AppleMusicKey, out string? au) ? au : null;
            string? deezerLink = dz is not null && dz.Links.TryGetValue(StreamingLinks.DeezerKey, out string? du) ? du : null;

            MergeLinks(artist, StreamingLinks.Build(artist.Name, appleLink, deezerLink));
            artist.PreviewUrl = previewUrl;

            clips += await StoreClipsAsync(db, artist, lookup, ct);

            await db.SaveChangesAsync(ct);

            attempted++;

            if (previewUrl is not null)
            {
                withPreview++;
            }

            if (attempted % 20 == 0)
            {
                _logger.LogInformation("Attempted {Attempted}/{Total}, {WithPreview} with a preview.",
                    attempted, pending.Count, withPreview);
            }
        }

        double pct = attempted == 0 ? 0 : 100.0 * withPreview / attempted;

        _logger.LogInformation(
            "Preview batch complete: {WithPreview}/{Attempted} resolved a preview ({Pct:F1}%), "
                + "{Clips} alternate clips stored, {Unavailable} skipped (both sources unreachable). "
                + "Re-run to continue.",
            withPreview, attempted, pct, clips, unavailable);
    }

    /// <summary>
    /// Phase two: alternate clips for bands that already have audio but were never harvested — the ones
    /// The Rite resolved just-in-time (D40), which is most of the audible catalogue.
    ///
    /// <para>
    /// The selection runs in SQL, and it matters that it can: an artist row carries a 768-dimension
    /// vector, and pulling the catalogue into memory to pick a few hundred is the mistake D61 found in
    /// <c>ListenersJob</c>. <c>preview_url IS NOT NULL AND previews_checked_at IS NULL</c> is a plain
    /// predicate over columns — the anti-join a jsonb list could not have offered.
    /// </para>
    /// <para>
    /// Silent bands are excluded by <c>preview_url IS NOT NULL</c>, deliberately: a band probed and
    /// found inaudible has nothing to harvest, and asking every run to rediscover that is the loop D61
    /// exists to stop. <b>This phase never writes <c>preview_url</c>.</b> The Rite draws on
    /// <c>preview_url IS NOT NULL</c>; touching it here could only ever subtract.
    /// </para>
    /// </summary>
    private async Task HarvestAsync(
        GrimoireDbContext db,
        IEnrichmentSource? itunes,
        IEnrichmentSource? deezer,
        CancellationToken ct)
    {
        int total = await db.Artists.CountAsync(a => a.PreviewUrl != null && a.PreviewsCheckedAt == null, ct);

        List<Artist> pending = await db.Artists
            .Where(a => a.PreviewUrl != null && a.PreviewsCheckedAt == null)
            .OrderBy(a => a.Name)
            .Take(_options.Limit)
            .ToListAsync(ct);

        _logger.LogInformation("{Pending} audible artists pending an alternate-clip harvest (of {Total}).",
            pending.Count, total);

        int harvested = 0;
        int clips = 0;
        int unavailable = 0;

        foreach (Artist artist in pending)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            Lookup lookup = await AskAsync(artist, itunes, deezer, ct);

            if (lookup.NobodyAnswered)
            {
                unavailable++;
                continue;
            }

            int added = await StoreClipsAsync(db, artist, lookup, ct);

            if (added == 0 && !lookup.EveryoneAnswered)
            {
                // Nothing gained, and the marker may not be stamped: there is nothing to save.
                unavailable++;
                continue;
            }

            await db.SaveChangesAsync(ct);

            clips += added;
            harvested++;

            if (harvested % 20 == 0)
            {
                _logger.LogInformation("Harvested {Harvested}/{Total}, {Clips} alternate clips stored.",
                    harvested, pending.Count, clips);
            }
        }

        _logger.LogInformation(
            "Harvest batch complete: {Harvested} artists harvested, {Clips} alternate clips stored, "
                + "{Unavailable} left unmarked (a source would not answer — a re-run retries them). "
                + "Re-run to continue.",
            harvested, clips, unavailable);
    }

    /// <summary>Asks both sources about one artist. A source that is off is simply not asked (D9).</summary>
    private static async Task<Lookup> AskAsync(
        Artist artist,
        IEnrichmentSource? itunes,
        IEnrichmentSource? deezer,
        CancellationToken ct)
    {
        EnrichmentResult apple = itunes is null
            ? EnrichmentResult.NoData
            : await itunes.FetchAsync(artist, ct);
        EnrichmentResult dz = deezer is null
            ? EnrichmentResult.NoData
            : await deezer.FetchAsync(artist, ct);

        return new Lookup(apple, dz);
    }

    /// <summary>
    /// Adds whatever clips this lookup found that the artist does not already hold, and stamps the
    /// harvest marker — but only when every source that was asked actually answered.
    ///
    /// <para>
    /// That last clause is the D61 lesson in one line. The marker's promise is "this band's alternates
    /// have been collected"; stamping it after a 429 makes that promise about an outage, and nothing
    /// ever revisits a stamped row. Clips found alongside a failure are still written — they are real —
    /// and the unstamped row simply comes round again, where the primary key turns the rewrite into
    /// nothing. A pass that spins is loud; a pass that seals a lie is silent, and D61 prefers loud.
    /// </para>
    /// </summary>
    private static async Task<int> StoreClipsAsync(
        GrimoireDbContext db,
        Artist artist,
        Lookup lookup,
        CancellationToken ct)
    {
        // Load what the band already holds, for this artist alone: Additions dedupes against it, and a
        // clip already stored would otherwise be an INSERT onto its own primary key. One small query
        // per artist, against a batch where every artist already costs a paced HTTP call.
        await db.Entry(artist).Collection(a => a.Previews).LoadAsync(ct);

        // iTunes first, Deezer after (D25): the order decides which clips survive the cap.
        List<PreviewCandidate> candidates =
        [
            .. lookup.Apple.Enrichment?.Previews ?? [],
            .. lookup.Deezer.Enrichment?.Previews ?? [],
        ];

        IReadOnlyList<ArtistPreview> additions = ArtistPreviews.Additions(
            artist.Id, artist.Previews, candidates, DateTime.UtcNow);

        foreach (ArtistPreview preview in additions)
        {
            artist.Previews.Add(preview);
        }

        if (lookup.EveryoneAnswered)
        {
            artist.PreviewsCheckedAt = DateTime.UtcNow;
        }

        return additions.Count;
    }

    private static void MergeLinks(Artist artist, IReadOnlyDictionary<string, string> curated)
    {
        // Additive merge: keep the raw MusicBrainz url-rels already present, add/refresh the
        // curated listen: links. A new dictionary instance makes the change detectable to EF.
        Dictionary<string, string> merged = artist.Links is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(artist.Links, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> link in curated)
        {
            merged[link.Key] = link.Value;
        }

        artist.Links = merged;
    }

    /// <summary>What both sources said about one artist, and the two questions the pass asks of that.</summary>
    private readonly record struct Lookup(EnrichmentResult Apple, EnrichmentResult Deezer)
    {
        /// <summary>Nobody answered at all: we learned nothing, so nothing may be written.</summary>
        public bool NobodyAnswered =>
            Apple.Outcome == EnrichmentOutcome.Unavailable && Deezer.Outcome == EnrichmentOutcome.Unavailable;

        /// <summary>Every source gave a definitive answer — the only state in which a marker may be sealed (D61).</summary>
        public bool EveryoneAnswered =>
            Apple.Outcome != EnrichmentOutcome.Unavailable && Deezer.Outcome != EnrichmentOutcome.Unavailable;
    }
}
