using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Wikidata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikidata;

/// <summary>
/// Populates <c>artist_edges</c> with <c>influenced_by</c> relations from Wikidata P737
/// (feature B16, the influence side of Bloodline). Only artists that carry a Wikidata QID in
/// <c>links['wikidata']</c> are considered, and only influence pairs whose <b>both</b> endpoints
/// are in our corpus become edges — Wikidata nodes we do not have are dropped, never invented
/// (autonomous-mode rule). The edge is directed <c>From</c> (the influenced artist)
/// influenced_by <c>To</c> (the influencer). QIDs are queried in batches via a SPARQL
/// <c>VALUES</c> clause, so the endpoint sees a handful of requests. Idempotent: edges upsert by
/// (from, to, influenced_by).
/// </summary>
public sealed class InfluenceJob : WorkerJob
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikidataClient _client;
    private readonly ILogger<InfluenceJob> _logger;

    public InfluenceJob(
        IServiceScopeFactory scopeFactory,
        WikidataClient client,
        IHostApplicationLifetime lifetime,
        ILogger<InfluenceJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    protected override string CommandName => "Influence import";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        Dictionary<string, Guid> qidToArtist = await BuildQidMapAsync(db, ct);

        _logger.LogInformation("{Count} artists carry a Wikidata QID. Querying P737 in batches of {Batch}...",
            qidToArtist.Count, BatchSize);

        if (qidToArtist.Count == 0)
        {
            _logger.LogWarning("No Wikidata QIDs in the corpus; nothing to query.");
            return;
        }

        List<string> qids = [.. qidToArtist.Keys];
        List<WikidataInfluence.Pair> allPairs = [];
        int batches = 0;

        for (int i = 0; i < qids.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            IEnumerable<string> batch = qids.Skip(i).Take(BatchSize);
            SparqlResponse? response = await _client.QueryAsync(WikidataQueries.Influence(batch), ct);
            allPairs.AddRange(WikidataInfluence.Parse(response));
            batches++;
        }

        List<WikidataInfluence.Edge> edges = WikidataInfluence.ToEdges(allPairs, qidToArtist);

        _logger.LogInformation(
            "Queried {Batches} batches, {Pairs} raw influence pairs, {Edges} within the corpus.",
            batches, allPairs.Count, edges.Count);

        (int inserted, int existing) = await WriteAsync(db, edges, ct);

        _logger.LogInformation(
            "Influence complete: {Inserted} edges inserted, {Existing} already present.",
            inserted, existing);
    }

    private static async Task<Dictionary<string, Guid>> BuildQidMapAsync(GrimoireDbContext db, CancellationToken ct)
    {
        // Links is a value-converted jsonb string, so its 'wikidata' key cannot be filtered in SQL.
        // The corpus is small (~2.5k rows), so id + links are pulled and filtered in memory.
        // The map is QID -> artist id; a QID on more than one artist (it should not be) keeps the
        // first by name, deterministically.
        var rows = await db.Artists
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Links })
            .ToListAsync(ct);

        Dictionary<string, Guid> map = new(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (row.Links is null || !row.Links.TryGetValue("wikidata", out string? link))
            {
                continue;
            }

            string? qid = WikidataQid.FromUri(link);

            if (qid is not null)
            {
                map.TryAdd(qid, row.Id);
            }
        }

        return map;
    }

    private static async Task<(int Inserted, int Existing)> WriteAsync(
        GrimoireDbContext db,
        List<WikidataInfluence.Edge> edges,
        CancellationToken ct)
    {
        HashSet<(Guid, Guid)> existingKeys = (await db.ArtistEdges
                .Where(e => e.Kind == EdgeKind.InfluencedBy)
                .Select(e => new { e.FromId, e.ToId })
                .ToListAsync(ct))
            .Select(e => (e.FromId, e.ToId))
            .ToHashSet();

        int inserted = 0;
        int existing = 0;

        foreach (WikidataInfluence.Edge edge in edges)
        {
            if (existingKeys.Contains((edge.FromId, edge.ToId)))
            {
                existing++;
                continue;
            }

            db.ArtistEdges.Add(new ArtistEdge
            {
                Id = Guid.NewGuid(),
                FromId = edge.FromId,
                ToId = edge.ToId,
                Kind = EdgeKind.InfluencedBy,
            });

            existingKeys.Add((edge.FromId, edge.ToId));
            inserted++;
        }

        await db.SaveChangesAsync(ct);

        return (inserted, existing);
    }
}
