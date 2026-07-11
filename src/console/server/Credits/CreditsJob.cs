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
/// Populates the <c>credits</c> table (feature B9): for each of our release-groups it fetches one
/// concrete release from MusicBrainz with recording-level artist relations, and records who played
/// what — performers with instruments, vocalists, producers, engineers — distinguishing official
/// members from guests (SPEC §4). Only artists already in our corpus are credited (matched by MBID);
/// anything external is discarded, never invented. Every credit carries <c>source='musicbrainz'</c>
/// and <c>confidence=1</c> (a direct source fact, D9).
///
/// The pass is <b>batched</b> (<see cref="CreditsOptions.Limit"/> release-groups at MusicBrainz's
/// strict 1 req/s), <b>resumable</b> (a disk ledger skips finished groups; the release JSON is
/// cached and shared with the labels pass) and <b>idempotent</b> (a group's credits are rewritten
/// wholesale, keyed by its release id). It declares how many release-groups remain.
/// </summary>
public sealed class CreditsJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MusicBrainzClient _client;
    private readonly EtlCache _cache;
    private readonly CreditsOptions _options;
    private readonly ILogger<CreditsJob> _logger;

    public CreditsJob(
        IServiceScopeFactory scopeFactory,
        MusicBrainzClient client,
        EtlCache cache,
        CreditsOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<CreditsJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Credits import";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        // The corpus, indexed by MBID: the credit's artist must already be one of ours.
        Dictionary<Guid, Guid> mbidToArtistId = await db.Artists
            .Where(a => a.Mbid != Guid.Empty)
            .Select(a => new { a.Mbid, a.Id })
            .ToDictionaryAsync(a => a.Mbid, a => a.Id, ct);

        HashSet<Guid> corpusMbids = [.. mbidToArtistId.Keys];

        List<ReleaseRef> releases = await db.Releases
            .Where(r => r.Mbid != Guid.Empty)
            .OrderBy(r => r.Id)
            .Select(r => new ReleaseRef(r.Id, r.Mbid))
            .ToListAsync(ct);

        ProgressLedger ledger = _cache.Ledger("credits");

        List<ReleaseRef> pending = releases.Where(r => !ledger.Contains(r.Mbid)).ToList();
        List<ReleaseRef> batch = pending.Take(_options.Limit).ToList();

        _logger.LogInformation(
            "Credits: {Total} release-groups, {Done} already done, {Pending} pending. Processing {Batch} this pass at 1 req/s (cache {Cache}).",
            releases.Count, ledger.Count, pending.Count, batch.Count, _cache.Root);

        int processed = 0;
        int fetched = 0;
        int creditsWritten = 0;
        int guestCredits = 0;

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
                detail = await FetchAsync(release.Mbid, ct);
                fetched++;
            }

            List<Credit> credits = BuildCredits(detail, release.Id, corpusMbids, mbidToArtistId);

            // Idempotent per release: replace this release's credits wholesale.
            await db.Credits.Where(c => c.ReleaseId == release.Id).ExecuteDeleteAsync(ct);

            if (credits.Count > 0)
            {
                db.Credits.AddRange(credits);
                await db.SaveChangesAsync(ct);
            }

            await ledger.MarkAsync(release.Mbid, ct);

            processed++;
            creditsWritten += credits.Count;
            guestCredits += credits.Count(c => c.IsGuest);

            if (processed % 25 == 0)
            {
                _logger.LogInformation("Credits: processed {Processed}/{Batch}, {Written} credits so far.",
                    processed, batch.Count, creditsWritten);
            }
        }

        int remaining = pending.Count - processed;

        _logger.LogInformation(
            "Credits complete: processed {Processed} release-groups ({Fetched} fetched, rest from cache), "
            + "{Written} credits written ({Guests} as guests). {Remaining} release-groups still pending.",
            processed, fetched, creditsWritten, guestCredits, remaining);
    }

    private async Task<MbRelease?> FetchAsync(Guid releaseGroupMbid, CancellationToken ct)
    {
        ReleaseBrowseResponse? browse = await _client.GetReleasesForCreditsAsync(
            releaseGroupMbid.ToString(), _options.ReleasesPerGroup, ct);

        MbRelease? best = PickBest(browse?.Releases);
        await _cache.SaveReleaseAsync(releaseGroupMbid, best, ct);
        return best;
    }

    /// <summary>
    /// Picks the release to draw credits from: prefer an official status, then the earliest date,
    /// then the one with the most recordings that carry relations (the fullest credited edition).
    /// </summary>
    private static MbRelease? PickBest(List<MbRelease>? releases)
    {
        if (releases is null || releases.Count == 0)
        {
            return null;
        }

        return releases
            .OrderByDescending(r => string.Equals(r.Status, "Official", StringComparison.OrdinalIgnoreCase))
            .ThenBy(r => r.Date ?? "9999")
            .ThenByDescending(RelationRichness)
            .First();
    }

    private static int RelationRichness(MbRelease release)
    {
        int count = release.Relations?.Count ?? 0;

        if (release.Media is null)
        {
            return count;
        }

        foreach (MbMedium medium in release.Media)
        {
            if (medium.Tracks is null)
            {
                continue;
            }

            foreach (MbTrack track in medium.Tracks)
            {
                count += track.Recording?.Relations?.Count ?? 0;
            }
        }

        return count;
    }

    private static List<Credit> BuildCredits(
        MbRelease? release,
        Guid releaseId,
        IReadOnlySet<Guid> corpusMbids,
        IReadOnlyDictionary<Guid, Guid> mbidToArtistId)
    {
        List<Credit> credits = [];

        if (release is null)
        {
            return credits;
        }

        // Distinct within a release: the same person can appear once per track, which is real, but
        // an identical (artist, recording, role, instrument, guest) tuple is deduped.
        HashSet<(Guid, Guid?, string, string, bool)> seen = [];

        void AddRelation(string? type, IReadOnlyList<string>? attributes, MbArtist? artist, Guid? recordingMbid)
        {
            if (artist is null || !Guid.TryParse(artist.Id, out Guid artistMbid))
            {
                return;
            }

            foreach (ResolvedCredit resolved in CreditResolver.Resolve(type, attributes, artistMbid, recordingMbid, corpusMbids))
            {
                if (!mbidToArtistId.TryGetValue(resolved.ArtistMbid, out Guid artistId))
                {
                    continue;
                }

                var key = (artistId, resolved.RecordingMbid, resolved.Role, resolved.Instrument ?? string.Empty, resolved.IsGuest);

                if (!seen.Add(key))
                {
                    continue;
                }

                credits.Add(new Credit
                {
                    Id = Guid.NewGuid(),
                    ArtistId = artistId,
                    ReleaseId = releaseId,
                    RecordingId = resolved.RecordingMbid,
                    Role = resolved.Role,
                    Instrument = resolved.Instrument,
                    IsGuest = resolved.IsGuest,
                    Source = "musicbrainz",
                    Confidence = 1f,
                });
            }
        }

        // Billed artists (the act on the cover): performer credits, release-level.
        if (release.ArtistCredit is not null)
        {
            foreach (MbArtistCredit credit in release.ArtistCredit)
            {
                AddRelation(CreditResolver.RolePerformer, null, credit.Artist, null);
            }
        }

        // Release-level production relations (producer, engineer, mix, master).
        if (release.Relations is not null)
        {
            foreach (MbRelation relation in release.Relations)
            {
                AddRelation(relation.Type, relation.Attributes, relation.Artist, null);
            }
        }

        // Recording-level relations: the per-track performers and their instruments.
        if (release.Media is not null)
        {
            foreach (MbMedium medium in release.Media)
            {
                if (medium.Tracks is null)
                {
                    continue;
                }

                foreach (MbTrack track in medium.Tracks)
                {
                    MbRecording? recording = track.Recording;

                    if (recording is null || !Guid.TryParse(recording.Id, out Guid recordingMbid) || recording.Relations is null)
                    {
                        continue;
                    }

                    foreach (MbRelation relation in recording.Relations)
                    {
                        AddRelation(relation.Type, relation.Attributes, relation.Artist, recordingMbid);
                    }
                }
            }
        }

        return credits;
    }

    private readonly record struct ReleaseRef(Guid Id, Guid Mbid);
}
