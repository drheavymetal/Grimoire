using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Worker.MusicBrainz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Classical;

/// <summary>
/// Seeds the classical model (movement VII, D11): a curated set of canonical composers resolved by
/// unambiguous exact name (<see cref="ComposerResolver"/>, same discipline as the D23 anchors — a
/// name that is not exactly one Person is logged and skipped, never guessed), their works into the
/// <c>works</c> table (associated to the composer through <c>works.composer_id</c>), and the
/// teacher/student edges MusicBrainz documents between people. Every MusicBrainz call goes through
/// the shared 1 req/s limiter. Idempotent and resumable: artists and works upsert by MBID, edges by
/// (from, to, kind).
/// </summary>
public sealed class ClassicalJob : WorkerJob
{
    /// <summary>
    /// Canonical composers spread across eras (Baroque → contemporary), chosen also for pedagogical
    /// connectivity so the teacher/student graph is not empty: Haydn taught Beethoven, Fauré taught
    /// Ravel, Schoenberg taught Berg and Webern, Boulanger taught Glass. Each is resolved by exact
    /// name only; an ambiguous or missing name is skipped, never guessed.
    /// </summary>
    // Names are given in the form MusicBrainz records for the entity — its primary name or a
    // recorded alias (the resolver matches either, diacritic-insensitively). MusicBrainz stores
    // several composers under their native spelling: Chopin as "Fryderyk Chopin", Schoenberg as
    // "Arnold Schönberg" (ö, not "oe"), and the Russians under their Cyrillic primary name with no
    // exact Latin alias — so those are given as MusicBrainz holds them, to resolve without guessing.
    private static readonly string[] ComposerNames =
    [
        // Baroque
        "Johann Sebastian Bach",
        "Antonio Vivaldi",
        "George Frideric Handel",
        // Classical
        "Joseph Haydn",
        "Wolfgang Amadeus Mozart",
        "Ludwig van Beethoven",
        "Antonio Salieri",
        // Romantic
        "Fryderyk Chopin",
        "Richard Wagner",
        "Johannes Brahms",
        "Gustav Mahler",
        "Gabriel Fauré",
        "Jean Sibelius",
        // Impressionist
        "Claude Debussy",
        "Maurice Ravel",
        // Modern / second Viennese school
        "Игорь Фёдорович Стравинский", // Igor Stravinsky (Cyrillic primary; no exact Latin alias)
        "Arnold Schönberg",
        "Alban Berg",
        "Anton Webern",
        "Bartók Béla", // Béla Bartók (MusicBrainz primary is the Hungarian name order)
        "Дмитрий Дмитриевич Шостакович", // Dmitri Shostakovich (Cyrillic primary; no exact Latin alias)
        "Nadia Boulanger",
        // Contemporary / minimalist
        "György Ligeti",
        "Arvo Pärt",
        "Philip Glass",
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MusicBrainzClient _client;
    private readonly ClassicalOptions _options;
    private readonly ILogger<ClassicalJob> _logger;

    public ClassicalJob(
        IServiceScopeFactory scopeFactory,
        MusicBrainzClient client,
        ClassicalOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<ClassicalJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Classical seed";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        Dictionary<Guid, Artist> byMbid = (await db.Artists.ToListAsync(ct)).ToDictionary(a => a.Mbid);

        // Phase 1 — resolve + upsert composers.
        List<Guid> composerMbids = await SeedComposersAsync(db, byMbid, ct);

        _logger.LogInformation(
            "Composers: {Resolved}/{Requested} resolved and upserted.",
            composerMbids.Count, ComposerNames.Length);

        // Phase 2 — works per composer.
        int worksInserted = await SeedWorksAsync(db, composerMbids, ct);

        // Phase 3 — teacher/student edges among the corpus.
        int teacherEdges = await SeedTeacherEdgesAsync(db, byMbid, composerMbids, ct);

        _logger.LogInformation(
            "Classical seed complete: {Composers} composers, {Works} works inserted, {Edges} teacher/student edges inserted.",
            composerMbids.Count, worksInserted, teacherEdges);
    }

    private async Task<List<Guid>> SeedComposersAsync(
        GrimoireDbContext db,
        Dictionary<Guid, Artist> byMbid,
        CancellationToken ct)
    {
        List<Guid> resolved = [];

        foreach (string name in ComposerNames)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            ArtistSearchResponse? search = await _client.SearchArtistByNameAsync(name, ct);

            if (search is null)
            {
                _logger.LogWarning("Composer '{Name}': search returned no response; skipped.", name);
                continue;
            }

            IEnumerable<ComposerCandidate> candidates = search.Artists
                .Select(a => new ComposerCandidate(
                    a.Id,
                    a.Name,
                    a.SortName,
                    a.Type,
                    a.Aliases?.Select(x => x.Name ?? string.Empty).ToArray() ?? []));

            ComposerMatch match = ComposerResolver.Resolve(name, candidates);

            if (match.Status != ComposerMatchStatus.Resolved || match.Mbid is null)
            {
                _logger.LogWarning("Composer '{Name}': {Status}; skipped (not guessed).", name, match.Status);
                continue;
            }

            if (!Guid.TryParse(match.Mbid, out Guid composerMbid))
            {
                _logger.LogWarning("Composer '{Name}': resolved MBID '{Mbid}' is not a GUID; skipped.", name, match.Mbid);
                continue;
            }

            await UpsertComposerAsync(db, byMbid, composerMbid, ct);
            resolved.Add(composerMbid);

            _logger.LogInformation("Composer '{Name}' resolved to {Mbid}.", name, composerMbid);
        }

        return resolved.Distinct().ToList();
    }

    private async Task UpsertComposerAsync(
        GrimoireDbContext db,
        Dictionary<Guid, Artist> byMbid,
        Guid composerMbid,
        CancellationToken ct)
    {
        // Fetch detail (tags + url-rels) so the composer row carries country, tags and a Wikidata QID
        // (which the existing 'influence' verb can later use for P737 among composers).
        MbArtist? detail = await _client.GetArtistAsync(composerMbid.ToString(), ct);

        if (!byMbid.TryGetValue(composerMbid, out Artist? artist))
        {
            artist = new Artist { Id = Guid.NewGuid(), Mbid = composerMbid };
            db.Artists.Add(artist);
            byMbid[composerMbid] = artist;
        }

        artist.Name = detail?.Name ?? artist.Name;
        artist.SortName = detail?.SortName ?? artist.SortName;

        // A composer is a Person. MusicBrainz should say so; force it for the classical model since
        // the resolver already required an exact Person match.
        artist.Kind = ArtistKind.Person;
        artist.Country = detail?.Country ?? artist.Country;
        artist.City = detail?.BeginArea?.Name ?? detail?.Area?.Name ?? artist.City;
        artist.FormedYear = ParseYear(detail?.LifeSpan?.Begin) ?? artist.FormedYear;
        artist.DissolvedYear = ParseYear(detail?.LifeSpan?.End) ?? artist.DissolvedYear;

        if (detail?.Tags is { Count: > 0 })
        {
            artist.Tags = detail.Tags
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .OrderByDescending(t => t.Count)
                .Select(t => t.Name)
                .Distinct()
                .ToArray();
        }

        Dictionary<string, string>? links = MbMapping.MapLinks(detail?.Relations);

        if (links is not null)
        {
            artist.Links = artist.Links is null
                ? links
                : MergeLinks(artist.Links, links);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<int> SeedWorksAsync(
        GrimoireDbContext db,
        List<Guid> composerMbids,
        CancellationToken ct)
    {
        // Existing works keyed by MBID for idempotent upsert. works.mbid is globally unique, so a work
        // co-credited to several composers is attributed to the FIRST composer that imports it.
        Dictionary<Guid, Work> existingByMbid = await db.Works.ToDictionaryAsync(w => w.Mbid, ct);

        int inserted = 0;

        foreach (Guid composerMbid in composerMbids)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            Artist composer = (await db.Artists.FirstAsync(a => a.Mbid == composerMbid, ct));

            WorkBrowseResponse? browse = await _client.GetWorksForArtistAsync(
                composerMbid.ToString(), _options.WorksPerComposer, ct);

            if (browse is null)
            {
                _logger.LogWarning("Composer {Mbid}: work browse failed; skipped.", composerMbid);
                continue;
            }

            int addedForComposer = 0;

            foreach (MbWork mbWork in browse.Works)
            {
                Work? mapped = WorkMapper.Map(mbWork.Id, mbWork.Title, mbWork.Type, composer.Id);

                if (mapped is null)
                {
                    continue;
                }

                if (existingByMbid.TryGetValue(mapped.Mbid, out Work? existing))
                {
                    // Update title/kind; keep the first composer that claimed it (do not overwrite).
                    existing.Title = mapped.Title;
                    existing.Kind = mapped.Kind;
                    existing.ComposerId ??= composer.Id;
                    continue;
                }

                db.Works.Add(mapped);
                existingByMbid[mapped.Mbid] = mapped;
                addedForComposer++;
            }

            inserted += addedForComposer;

            _logger.LogInformation(
                "Composer {Mbid}: {Added} new works ({Total} in browse of {Count}).",
                composerMbid, addedForComposer, browse.Works.Count, browse.Count);

            await db.SaveChangesAsync(ct);
        }

        return inserted;
    }

    private async Task<int> SeedTeacherEdgesAsync(
        GrimoireDbContext db,
        Dictionary<Guid, Artist> byMbid,
        List<Guid> composerMbids,
        CancellationToken ct)
    {
        // Collect canonical (teacher, student) pairs. Querying either endpoint yields the same pair,
        // so a set dedupes them. Only pairs whose BOTH endpoints are already in our corpus survive
        // (no expansion — brief): a teacher outside the corpus is dropped, never invented.
        HashSet<TeacherStudentPair> pairs = [];

        foreach (Guid composerMbid in composerMbids)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            MbArtist? detail = await _client.GetArtistRelationsAsync(composerMbid.ToString(), ct);

            if (detail?.Relations is null)
            {
                continue;
            }

            foreach (MbRelation relation in detail.Relations)
            {
                if (relation.Artist is null || !Guid.TryParse(relation.Artist.Id, out Guid targetMbid))
                {
                    continue;
                }

                TeacherStudentPair? pair = TeacherStudentResolver.Resolve(
                    relation.Type, relation.Direction, composerMbid, targetMbid);

                if (pair is null)
                {
                    continue;
                }

                // Both endpoints must be in the corpus.
                if (byMbid.ContainsKey(pair.Value.TeacherMbid) && byMbid.ContainsKey(pair.Value.StudentMbid))
                {
                    pairs.Add(pair.Value);
                }
            }
        }

        return await WriteTeacherEdgesAsync(db, byMbid, pairs, ct);
    }

    private static async Task<int> WriteTeacherEdgesAsync(
        GrimoireDbContext db,
        Dictionary<Guid, Artist> byMbid,
        HashSet<TeacherStudentPair> pairs,
        CancellationToken ct)
    {
        // Existing teacher/student edges for idempotent upsert.
        HashSet<(Guid, Guid, EdgeKind)> existing = (await db.ArtistEdges
                .Where(e => e.Kind == EdgeKind.Teacher || e.Kind == EdgeKind.Student)
                .Select(e => new { e.FromId, e.ToId, e.Kind })
                .ToListAsync(ct))
            .Select(e => (e.FromId, e.ToId, e.Kind))
            .ToHashSet();

        int inserted = 0;

        foreach (TeacherStudentPair pair in pairs)
        {
            Guid teacherId = byMbid[pair.TeacherMbid].Id;
            Guid studentId = byMbid[pair.StudentMbid].Id;

            // Each pedagogical link materialises as two directed edges so a composer's page can list
            // both "taught" (From = self, Kind = Teacher) and "studied under" (From = self, Kind = Student)
            // with a single indexed lookup. Both enum values (Teacher, Student) are used by design.
            inserted += TryAddEdge(db, existing, teacherId, studentId, EdgeKind.Teacher);
            inserted += TryAddEdge(db, existing, studentId, teacherId, EdgeKind.Student);
        }

        await db.SaveChangesAsync(ct);

        return inserted;
    }

    private static int TryAddEdge(
        GrimoireDbContext db,
        HashSet<(Guid, Guid, EdgeKind)> existing,
        Guid fromId,
        Guid toId,
        EdgeKind kind)
    {
        if (!existing.Add((fromId, toId, kind)))
        {
            return 0;
        }

        db.ArtistEdges.Add(new ArtistEdge
        {
            Id = Guid.NewGuid(),
            FromId = fromId,
            ToId = toId,
            Kind = kind,
        });

        return 1;
    }

    private static Dictionary<string, string> MergeLinks(
        Dictionary<string, string> existing,
        Dictionary<string, string> incoming)
    {
        Dictionary<string, string> merged = new(existing, StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> link in incoming)
        {
            merged[link.Key] = link.Value;
        }

        return merged;
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
        {
            return null;
        }

        return int.TryParse(value.AsSpan(0, 4), out int year) ? year : null;
    }
}
