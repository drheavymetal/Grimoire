using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Worker.MusicBrainz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Credits;

/// <summary>
/// Populates the <c>labels</c> table and <c>releases.label_id</c> (features B20/B21) from the
/// label-info of each release-group's chosen release. The release JSON is shared with the credits
/// pass through the disk cache, so once credits has run this pass needs no MusicBrainz calls for
/// those groups; groups not yet cached are fetched here, bounded by <see cref="CreditsOptions.Limit"/>.
/// Label country — which label-info omits — is filled by a bounded second pass of label lookups.
/// Labels are keyed by MBID; anything without a well-formed MBID and name is left as a null
/// <c>label_id</c>, a real gap, never invented. Resumable (a disk ledger) and idempotent.
/// </summary>
public sealed class LabelsJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MusicBrainzClient _client;
    private readonly EtlCache _cache;
    private readonly CreditsOptions _options;
    private readonly ILogger<LabelsJob> _logger;

    public LabelsJob(
        IServiceScopeFactory scopeFactory,
        MusicBrainzClient client,
        EtlCache cache,
        CreditsOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<LabelsJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Labels import";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        List<ReleaseRef> releases = await db.Releases
            .Where(r => r.Mbid != Guid.Empty)
            .OrderBy(r => r.Id)
            .Select(r => new ReleaseRef(r.Id, r.Mbid))
            .ToListAsync(ct);

        ProgressLedger ledger = _cache.Ledger("labels");

        Dictionary<Guid, Label> labelsByMbid = await db.Labels.ToDictionaryAsync(l => l.Mbid, ct);

        List<ReleaseRef> pending = releases.Where(r => !ledger.Contains(r.Mbid)).ToList();

        // Prefer release-groups already cached by the credits pass (free); top up with fetches
        // up to the batch limit so this pass can also run standalone.
        List<ReleaseRef> cached = pending.Where(r => _cache.HasRelease(r.Mbid)).ToList();
        List<ReleaseRef> uncached = pending.Where(r => !_cache.HasRelease(r.Mbid)).Take(_options.Limit).ToList();
        List<ReleaseRef> batch = [.. cached, .. uncached];

        _logger.LogInformation(
            "Labels: {Total} release-groups, {Done} done, {Pending} pending ({Cached} cached, fetching up to {Fetch}).",
            releases.Count, ledger.Count, pending.Count, cached.Count, uncached.Count);

        List<(Guid ReleaseId, Guid LabelMbid)> assignments = [];
        List<Guid> processedGroups = [];
        int fetched = 0;

        foreach (ReleaseRef release in batch)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            MbRelease? detail;

            if (_cache.HasRelease(release.Mbid))
            {
                detail = await _cache.LoadReleaseAsync(release.Mbid, ct);
            }
            else
            {
                ReleaseBrowseResponse? browse = await _client.GetReleasesForCreditsAsync(
                    release.Mbid.ToString(), _options.ReleasesPerGroup, ct);
                detail = browse?.Releases.FirstOrDefault();
                await _cache.SaveReleaseAsync(release.Mbid, detail, ct);
                fetched++;
            }

            ResolvedLabel? label = ResolveLabel(detail);

            if (label is not null)
            {
                if (!labelsByMbid.TryGetValue(label.Mbid, out Label? existing))
                {
                    existing = new Label { Id = Guid.NewGuid(), Mbid = label.Mbid, Name = label.Name };
                    db.Labels.Add(existing);
                    labelsByMbid[label.Mbid] = existing;
                }
                else
                {
                    existing.Name = label.Name;
                }

                assignments.Add((release.Id, label.Mbid));
            }

            processedGroups.Add(release.Mbid);
        }

        // Persist new/updated labels first so the label_id foreign key resolves.
        await db.SaveChangesAsync(ct);

        int releasesLabelled = 0;

        foreach (IGrouping<Guid, (Guid ReleaseId, Guid LabelMbid)> group in assignments.GroupBy(a => a.LabelMbid))
        {
            Guid labelId = labelsByMbid[group.Key].Id;
            List<Guid> releaseIds = group.Select(a => a.ReleaseId).ToList();

            releasesLabelled += await db.Releases
                .Where(r => releaseIds.Contains(r.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LabelId, labelId), ct);
        }

        foreach (Guid rgMbid in processedGroups)
        {
            await ledger.MarkAsync(rgMbid, ct);
        }

        int labelsWithCountry = await EnrichCountriesAsync(db, ct);

        int remaining = pending.Count - processedGroups.Count;
        int labelsMissingCountry = await db.Labels.CountAsync(l => l.Country == null, ct);

        _logger.LogInformation(
            "Labels complete: processed {Processed} release-groups ({Fetched} fetched), {Labels} distinct labels, "
            + "{Labelled} releases got a label_id, {WithCountry} label countries filled this pass "
            + "({MissingCountry} still without country). {Remaining} release-groups still pending.",
            processedGroups.Count, fetched, labelsByMbid.Count, releasesLabelled, labelsWithCountry,
            labelsMissingCountry, remaining);
    }

    private async Task<int> EnrichCountriesAsync(GrimoireDbContext db, CancellationToken ct)
    {
        List<Label> needCountry = await db.Labels
            .Where(l => l.Country == null)
            .OrderBy(l => l.Id)
            .Take(_options.LabelCountryLimit)
            .ToListAsync(ct);

        int filled = 0;

        foreach (Label label in needCountry)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            MbLabel? mb = await _client.GetLabelAsync(label.Mbid.ToString(), ct);

            if (mb?.Country is { Length: > 0 } country)
            {
                label.Country = country;
                filled++;
            }
        }

        await db.SaveChangesAsync(ct);
        return filled;
    }

    private static ResolvedLabel? ResolveLabel(MbRelease? release)
    {
        if (release?.LabelInfo is null)
        {
            return null;
        }

        return LabelResolver.First(release.LabelInfo
            .Where(li => li.Label is not null)
            .Select(li => (li.Label!.Id, li.Label!.Name, (string?)null)));
    }

    private readonly record struct ReleaseRef(Guid Id, Guid Mbid);
}
