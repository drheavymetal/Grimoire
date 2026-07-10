using System.Globalization;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Worker.MusicBrainz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker;

/// <summary>
/// Seeds Postgres with real artist data from the MusicBrainz WS/2 API. Per DECISIONS
/// D23 / SPEC section 2 the corpus is: explicit anchors (resolved by exact name) ∪ a
/// bounded tag search over the metal + folk-that-orbits-metal corpus. Every artist is
/// enriched with tags and external links, and its release-groups are imported. The run
/// is idempotent: artists and releases are upserted by MusicBrainz id.
/// </summary>
public class MusicBrainzSeedJob : IHostedService
{
    private const int SearchPageSize = 100;

    /// <summary>
    /// Anchors seeded by name regardless of tags (DECISIONS D23): several carry no metal
    /// tags at all (e.g. Wardruna, Skáld), so the tag search alone would miss them. Each
    /// is resolved by unambiguous exact-name match only; a name that does not resolve is
    /// logged and skipped, never guessed. "Tartalo Music" is deliberately NOT an anchor:
    /// MusicBrainz returns it as a Person and Pedro has not confirmed it (D23).
    /// </summary>
    private static readonly string[] AnchorNames =
    [
        "Wardruna",
        "Heilung",
        "Skáld",
        "Gealdýr",
        "Einar Selvik",
        "Danheim",
        "Myrkur",
        "Faun",
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MusicBrainzClient _client;
    private readonly SeedOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MusicBrainzSeedJob> _logger;

    private Task? _seedTask;

    public MusicBrainzSeedJob(
        IServiceScopeFactory scopeFactory,
        MusicBrainzClient client,
        SeedOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<MusicBrainzSeedJob> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _options = options;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.RunSeed)
        {
            _logger.LogInformation("Seed job idle: start the worker with the 'seed' verb to run it.");
            return Task.CompletedTask;
        }

        _seedTask = Task.Run(() => RunSeedAsync(_lifetime.ApplicationStopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_seedTask is not null)
        {
            await _seedTask;
        }
    }

    private async Task RunSeedAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

            await db.Database.MigrateAsync(ct);

            // Release-group ids are globally unique, but a split or various-artists
            // release-group is returned by the browse of every artist on it. We attach
            // each release to the first artist that imports it and skip it elsewhere,
            // which also keeps re-runs idempotent. Seed the set from any existing rows.
            HashSet<Guid> knownReleaseMbids = (await db.Releases.Select(r => r.Mbid).ToListAsync(ct)).ToHashSet();

            List<string> mbids = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            List<string> anchorMbids = await ResolveAnchorsAsync(ct);
            foreach (string mbid in anchorMbids)
            {
                if (seen.Add(mbid))
                {
                    mbids.Add(mbid);
                }
            }

            int anchorCount = mbids.Count;

            await GatherTagMatchesAsync(mbids, seen, ct);

            _logger.LogInformation(
                "Gathered {Total} artists to seed ({Resolved}/{Requested} anchors resolved, {Tagged} from tag search). Fetching detail...",
                mbids.Count, anchorCount, AnchorNames.Distinct(StringComparer.OrdinalIgnoreCase).Count(), mbids.Count - anchorCount);

            int inserted = 0;
            int updated = 0;
            int releasesUpserted = 0;

            foreach (string mbid in mbids)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                (bool isNew, int releaseCount) = await UpsertArtistAsync(db, mbid, knownReleaseMbids, ct);

                if (isNew)
                {
                    inserted++;
                }
                else
                {
                    updated++;
                }

                releasesUpserted += releaseCount;
            }

            _logger.LogInformation(
                "Seed complete: {Inserted} inserted, {Updated} updated, {Releases} releases upserted.",
                inserted, updated, releasesUpserted);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Seed cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seed failed.");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    private async Task<List<string>> ResolveAnchorsAsync(CancellationToken ct)
    {
        List<string> resolved = [];

        foreach (string name in AnchorNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ArtistSearchResponse? response = await _client.SearchArtistByNameAsync(name, ct);

            if (response is null)
            {
                _logger.LogWarning("Anchor '{Name}': search returned no response; skipped.", name);
                continue;
            }

            List<MbArtist> exact = response.Artists
                .Where(a => string.Equals(a.Name, name, StringComparison.InvariantCultureIgnoreCase))
                .Where(a => IsGroupOrPerson(a.Type))
                .ToList();

            if (exact.Count == 0)
            {
                _logger.LogWarning("Anchor '{Name}': no exact-name group/person match; skipped (not guessed).", name);
                continue;
            }

            // Distinct MBIDs: the same entity can appear twice in the results.
            List<string> distinctIds = exact.Select(a => a.Id).Distinct().ToList();

            if (distinctIds.Count > 1)
            {
                _logger.LogWarning(
                    "Anchor '{Name}': {Count} distinct exact matches ({Ids}); ambiguous, skipped (not guessed).",
                    name, distinctIds.Count, string.Join(", ", distinctIds));
                continue;
            }

            _logger.LogInformation("Anchor '{Name}' resolved to {Mbid} ({Type}).", name, distinctIds[0], exact[0].Type);
            resolved.Add(distinctIds[0]);
        }

        return resolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task GatherTagMatchesAsync(List<string> mbids, HashSet<string> seen, CancellationToken ct)
    {
        int offset = 0;

        while (mbids.Count < _options.Target && !ct.IsCancellationRequested)
        {
            ArtistSearchResponse? page = await _client.SearchArtistsAsync(offset, SearchPageSize, ct);

            if (page is null || page.Artists.Count == 0)
            {
                break;
            }

            foreach (MbArtist artist in page.Artists)
            {
                if (seen.Add(artist.Id))
                {
                    mbids.Add(artist.Id);
                }
            }

            offset += SearchPageSize;

            if (mbids.Count >= _options.Target || offset >= page.Count)
            {
                break;
            }
        }
    }

    private async Task<(bool IsNew, int ReleaseCount)> UpsertArtistAsync(
        GrimoireDbContext db,
        string mbid,
        HashSet<Guid> knownReleaseMbids,
        CancellationToken ct)
    {
        MbArtist? detail = await _client.GetArtistAsync(mbid, ct);

        if (detail is null)
        {
            _logger.LogWarning("Artist {Mbid}: detail fetch failed; skipped.", mbid);
            return (false, 0);
        }

        if (!Guid.TryParse(detail.Id, out Guid artistMbid))
        {
            _logger.LogWarning("Artist {Mbid}: invalid MBID; skipped.", mbid);
            return (false, 0);
        }

        Artist? artist = await db.Artists
            .Include(a => a.Releases)
            .FirstOrDefaultAsync(a => a.Mbid == artistMbid, ct);

        bool isNew = artist is null;

        if (artist is null)
        {
            artist = new Artist { Id = Guid.NewGuid(), Mbid = artistMbid };
            db.Artists.Add(artist);
        }

        artist.Name = detail.Name;
        artist.SortName = detail.SortName;
        artist.Kind = MapKind(detail.Type);
        artist.Country = detail.Country;
        artist.City = detail.BeginArea?.Name ?? detail.Area?.Name;
        artist.FormedYear = ParseYear(detail.LifeSpan?.Begin);
        artist.DissolvedYear = ParseYear(detail.LifeSpan?.End);
        artist.Tags = MapTags(detail.Tags);
        artist.Links = MapLinks(detail.Relations);

        ReleaseGroupResponse? releaseGroups = await _client.GetReleaseGroupsAsync(mbid, ct);
        int releaseCount = 0;

        if (releaseGroups is not null)
        {
            Dictionary<Guid, Release> existingByMbid = artist.Releases.ToDictionary(r => r.Mbid);

            foreach (MbReleaseGroup group in releaseGroups.ReleaseGroups)
            {
                ReleaseType? type = MapReleaseType(group.PrimaryType, group.SecondaryTypes);

                if (type is null || !Guid.TryParse(group.Id, out Guid releaseMbid))
                {
                    continue;
                }

                if (existingByMbid.TryGetValue(releaseMbid, out Release? release))
                {
                    // This artist already owns the release: update it in place.
                    release.Title = group.Title;
                    release.Type = type.Value;
                    release.ReleaseDate = ParseDate(group.FirstReleaseDate);
                    releaseCount++;
                    continue;
                }

                if (knownReleaseMbids.Contains(releaseMbid))
                {
                    // Already attached to another artist (split / various-artists); skip.
                    continue;
                }

                release = new Release
                {
                    Id = Guid.NewGuid(),
                    Mbid = releaseMbid,
                    ArtistId = artist.Id,
                    Title = group.Title,
                    Type = type.Value,
                    ReleaseDate = ParseDate(group.FirstReleaseDate),
                };
                artist.Releases.Add(release);
                knownReleaseMbids.Add(releaseMbid);
                releaseCount++;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return (isNew, releaseCount);
    }

    private static bool IsGroupOrPerson(string? type)
    {
        return string.Equals(type, "Group", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "Person", StringComparison.OrdinalIgnoreCase);
    }

    private static ArtistKind MapKind(string? type)
    {
        return type switch
        {
            "Person" => ArtistKind.Person,
            "Orchestra" => ArtistKind.Orchestra,
            "Choir" => ArtistKind.Choir,
            _ => ArtistKind.Group,
        };
    }

    private static string[] MapTags(List<MbTag>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .OrderByDescending(t => t.Count)
            .Select(t => t.Name)
            .Distinct()
            .ToArray();
    }

    private static Dictionary<string, string>? MapLinks(List<MbRelation>? relations)
    {
        if (relations is null)
        {
            return null;
        }

        Dictionary<string, string> links = new(StringComparer.OrdinalIgnoreCase);

        foreach (MbRelation relation in relations)
        {
            string? resource = relation.Url?.Resource;

            if (!string.IsNullOrWhiteSpace(relation.Type) && !string.IsNullOrWhiteSpace(resource))
            {
                links[relation.Type] = resource;
            }
        }

        return links.Count == 0 ? null : links;
    }

    private static ReleaseType? MapReleaseType(string? primary, List<string>? secondary)
    {
        if (secondary is not null)
        {
            if (secondary.Contains("Demo", StringComparer.OrdinalIgnoreCase))
            {
                return ReleaseType.Demo;
            }

            if (secondary.Contains("Compilation", StringComparer.OrdinalIgnoreCase))
            {
                return ReleaseType.Compilation;
            }

            if (secondary.Contains("Live", StringComparer.OrdinalIgnoreCase))
            {
                return ReleaseType.Live;
            }
        }

        return primary switch
        {
            "Album" => ReleaseType.Album,
            "EP" => ReleaseType.Ep,
            _ => null,
        };
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
        {
            return null;
        }

        return int.TryParse(value.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int year)
            ? year
            : null;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly full))
        {
            return full;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly month))
        {
            return month;
        }

        int? year = ParseYear(value);

        return year is null ? null : new DateOnly(year.Value, 1, 1);
    }
}
