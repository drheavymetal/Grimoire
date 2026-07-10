using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Worker.MusicBrainz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker;

/// <summary>
/// Populates <c>artist_edges</c> with official band memberships (member_of), each carrying
/// begin/end dates and instruments, from MusicBrainz <c>inc=artist-rels</c> (features B7/B8,
/// and the D23 admission criterion). Every currently-seeded artist is queried at the strict
/// 1 req/s cadence; the artist on the other end of each membership (a member, or a band a
/// seeded person played in) is inserted as a minimal row so the edge has both endpoints.
/// Idempotent: artists upsert by MBID, edges upsert by (from, to, member_of).
/// </summary>
public sealed class EdgesJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MusicBrainzClient _client;
    private readonly ILogger<EdgesJob> _logger;

    public EdgesJob(
        IServiceScopeFactory scopeFactory,
        MusicBrainzClient client,
        IHostApplicationLifetime lifetime,
        ILogger<EdgesJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    protected override string CommandName => "Edges import";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        List<Artist> artists = await db.Artists.ToListAsync(ct);
        Dictionary<Guid, Artist> byMbid = artists.ToDictionary(a => a.Mbid);

        // Query every artist already in the catalogue. Snapshot the MBIDs first: rows added
        // for newly-discovered members must not themselves be re-queried (one expansion layer).
        List<Guid> toQuery = artists.Select(a => a.Mbid).ToList();

        _logger.LogInformation("Fetching artist-rels for {Count} artists at 1 req/s...", toQuery.Count);

        // Aggregate memberships globally by (member, band): the same edge surfaces both when
        // the band is queried and when the member is, and a member may have multiple stints.
        Dictionary<(Guid Member, Guid Band), ResolvedMembership> memberships = new();
        int queried = 0;

        foreach (Guid mbid in toQuery)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (!byMbid.TryGetValue(mbid, out Artist? queriedArtist))
            {
                continue;
            }

            MbArtist? detail = await _client.GetArtistRelationsAsync(mbid.ToString(), ct);
            queried++;

            if (detail?.Relations is null)
            {
                continue;
            }

            foreach (MbRelation relation in detail.Relations)
            {
                MbArtist? target = relation.Artist;

                if (target is null || !Guid.TryParse(target.Id, out Guid targetMbid))
                {
                    continue;
                }

                ResolvedMembership? membership = MembershipResolver.Resolve(
                    relation.Type,
                    relation.Direction,
                    mbid,
                    queriedArtist.Name,
                    queriedArtist.SortName,
                    queriedArtist.Kind,
                    targetMbid,
                    target.Name,
                    target.SortName,
                    MbMapping.MapKind(target.Type),
                    relation.Begin,
                    relation.End,
                    relation.Attributes);

                if (membership is null)
                {
                    continue;
                }

                var key = (membership.MemberMbid, membership.BandMbid);

                memberships[key] = memberships.TryGetValue(key, out ResolvedMembership? existing)
                    ? MembershipResolver.Merge(existing, membership)
                    : membership;
            }

            if (queried % 50 == 0)
            {
                _logger.LogInformation("Queried {Queried}/{Total} artists, {Edges} distinct memberships so far.",
                    queried, toQuery.Count, memberships.Count);
            }
        }

        _logger.LogInformation("Resolved {Count} distinct memberships from {Queried} artists. Writing...",
            memberships.Count, queried);

        (int artistsAdded, int edgesInserted, int edgesUpdated) = await WriteAsync(db, byMbid, memberships, ct);

        _logger.LogInformation(
            "Edges complete: {Members} member rows added, {Inserted} edges inserted, {Updated} edges updated.",
            artistsAdded, edgesInserted, edgesUpdated);
    }

    private static async Task<(int ArtistsAdded, int EdgesInserted, int EdgesUpdated)> WriteAsync(
        GrimoireDbContext db,
        Dictionary<Guid, Artist> byMbid,
        Dictionary<(Guid Member, Guid Band), ResolvedMembership> memberships,
        CancellationToken ct)
    {
        // Existing edges keyed by (from, to, kind) for idempotent upsert.
        Dictionary<(Guid, Guid, EdgeKind), ArtistEdge> existingEdges = await db.ArtistEdges
            .Where(e => e.Kind == EdgeKind.MemberOf)
            .ToDictionaryAsync(e => (e.FromId, e.ToId, e.Kind), ct);

        int artistsAdded = 0;
        int edgesInserted = 0;
        int edgesUpdated = 0;

        foreach (ResolvedMembership m in memberships.Values)
        {
            Artist member = EnsureArtist(db, byMbid, m.MemberMbid, m.MemberName, m.MemberSortName, m.MemberKind, ref artistsAdded);
            Artist band = EnsureArtist(db, byMbid, m.BandMbid, m.BandName, null, ArtistKind.Group, ref artistsAdded);

            var key = (member.Id, band.Id, EdgeKind.MemberOf);

            if (existingEdges.TryGetValue(key, out ArtistEdge? edge))
            {
                edge.BeginDate = m.Begin;
                edge.EndDate = m.End;
                edge.Instruments = m.Instruments;
                edgesUpdated++;
            }
            else
            {
                edge = new ArtistEdge
                {
                    Id = Guid.NewGuid(),
                    FromId = member.Id,
                    ToId = band.Id,
                    Kind = EdgeKind.MemberOf,
                    BeginDate = m.Begin,
                    EndDate = m.End,
                    Instruments = m.Instruments,
                };
                db.ArtistEdges.Add(edge);
                existingEdges[key] = edge;
                edgesInserted++;
            }
        }

        await db.SaveChangesAsync(ct);

        return (artistsAdded, edgesInserted, edgesUpdated);
    }

    private static Artist EnsureArtist(
        GrimoireDbContext db,
        Dictionary<Guid, Artist> byMbid,
        Guid mbid,
        string name,
        string? sortName,
        ArtistKind kind,
        ref int added)
    {
        if (byMbid.TryGetValue(mbid, out Artist? artist))
        {
            return artist;
        }

        // A member (or band) discovered through a membership but not yet in the catalogue.
        // Minimal row: identity only. No tags, releases or embedding are invented for it.
        artist = new Artist
        {
            Id = Guid.NewGuid(),
            Mbid = mbid,
            Name = name,
            SortName = sortName,
            Kind = kind,
        };

        db.Artists.Add(artist);
        byMbid[mbid] = artist;
        added++;

        return artist;
    }
}
