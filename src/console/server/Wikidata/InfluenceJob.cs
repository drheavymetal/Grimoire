using System.Data.Common;
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
/// (Invariant 5). The edge is directed <c>From</c> (the influenced artist) influenced_by
/// <c>To</c> (the influencer).
/// <para>
/// <b>Why this pass produced 69 edges for a year.</b> It was written and run against the ~300-band
/// seed corpus, where the whole QID list was six batches and the run finished in seconds. At the
/// scale of the D5 import (46k QIDs, ~925 batches at the client's 2 s cadence = <b>31 minutes</b>)
/// it never finished — and it wrote <b>only after the last batch</b>, so an interrupted run
/// discarded everything it had learned and left the seed-era edges standing. Meanwhile every failed
/// batch resolved to a bare <c>null</c> that parsed into an empty list, so the pass reported success
/// either way. The evidence is in the data: the 26 artists holding those edges sit in 25 different
/// batches, and the other P737-carrying bands sharing those same batches have no edges at all —
/// no full-scale batch ever landed. It was written off as "Wikidata bulk falls over with 502/429";
/// a full sweep measured today answers <b>200 on every batch</b> and yields <b>2 043 edges</b>.
/// </para>
/// <para>
/// So: batches are large and go by POST (<see cref="WikidataClient"/>), each batch is
/// <b>written as it lands</b>, and a batch the endpoint failed to answer is counted and left for a
/// re-run rather than mistaken for "these bands influenced nobody" (D61). There is no marker table
/// and none is needed: the whole catalogue is ~47 requests, so a re-sweep is cheap and idempotent —
/// edges upsert by (from, to, influenced_by), guarded by the unique index on that triple.
/// </para>
/// </summary>
public sealed class InfluenceJob : WorkerJob
{
    // One thousand QIDs per SPARQL VALUES block. Measured against WDQS on the live catalogue:
    // 1 000 QIDs answer in ~0.44 s and 10 000 in ~3 s, both far inside the endpoint's 60 s query
    // budget and our 30 s HTTP timeout — the batch size is not what strains WDQS. What it buys is
    // politeness: 47 requests for the whole 46k-QID catalogue instead of 925. Kept at 1 000 rather
    // than pushed to the ceiling so a deferred batch loses little and the response stays small.
    private const int BatchSize = 1000;

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

        if (qidToArtist.Count == 0)
        {
            _logger.LogWarning("No Wikidata QIDs in the corpus; nothing to query.");
            return;
        }

        List<string> qids = [.. qidToArtist.Keys];
        int batchCount = (qids.Count + BatchSize - 1) / BatchSize;

        _logger.LogInformation(
            "{Count} artists carry a Wikidata QID. Querying P737 in {Batches} batches of {Batch}...",
            qidToArtist.Count, batchCount, BatchSize);

        HashSet<(Guid, Guid)> known = await LoadExistingEdgeKeysAsync(db, ct);

        _logger.LogInformation("{Known} influence edges already in the graph.", known.Count);

        int batches = 0;
        int deferred = 0;
        int pairs = 0;
        int inserted = 0;
        int alreadyPresent = 0;

        for (int i = 0; i < qids.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            List<string> batch = qids.GetRange(i, Math.Min(BatchSize, qids.Count - i));
            WikidataQueryResult result = await _client.QueryAsync(WikidataQueries.Influence(batch), ct);

            batches++;

            if (!result.Answered)
            {
                // WDQS did not answer. That says nothing about these 1 000 bands, so the batch is
                // simply not done — counted, reported, and left for a later run to sweep again.
                // There is nothing to stamp and so nothing to poison (D61).
                deferred++;
                continue;
            }

            List<WikidataInfluence.Pair> batchPairs = WikidataInfluence.Parse(result.Response);
            pairs += batchPairs.Count;

            // Written per batch, not once at the end. The single terminal write is what made a
            // 31-minute sweep all-or-nothing: killed at minute 30, it saved nothing at all.
            (int batchInserted, int batchExisting) =
                await WriteAsync(db, WikidataInfluence.ToEdges(batchPairs, qidToArtist), known, ct);

            inserted += batchInserted;
            alreadyPresent += batchExisting;

            _logger.LogInformation(
                "Batch {Batch}/{Total}: {Pairs} pairs, {Inserted} new edges ({TotalInserted} so far), "
                    + "{Deferred} deferred.",
                batches, batchCount, batchPairs.Count, batchInserted, inserted, deferred);
        }

        _logger.LogInformation(
            "Influence complete: {Batches}/{Total} batches, {Pairs} raw P737 pairs, {Inserted} edges inserted, "
                + "{Existing} already present, {Deferred} batches deferred (no answer).",
            batches, batchCount, pairs, inserted, alreadyPresent, deferred);

        if (deferred > 0)
        {
            _logger.LogWarning(
                "{Deferred} batches went unanswered and were NOT recorded as 'no influences'. Re-run to sweep them.",
                deferred);
        }
    }

    /// <summary>
    /// The QID -> artist id map, filtered in SQL. The catalogue holds 207k artists of which ~46k
    /// carry a Wikidata QID, so pulling every row and its <c>links</c> jsonb into memory to discard
    /// three quarters of them — which is what this did while its comment still claimed the corpus was
    /// "~2.5k rows" — is the same disease cured in <c>ListenersJob</c> and <c>StatsJob</c> (D63).
    /// <c>links</c> is a real jsonb column that EF only sees through a value converter, so the filter
    /// is expressed in raw SQL rather than LINQ. A QID on more than one artist (it should not be)
    /// keeps the first by name, deterministically.
    /// </summary>
    private static async Task<Dictionary<string, Guid>> BuildQidMapAsync(GrimoireDbContext db, CancellationToken ct)
    {
        Dictionary<string, Guid> map = new(StringComparer.Ordinal);

        await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using DbCommand command = db.Database.GetDbConnection().CreateCommand();

            command.CommandText =
                """
                SELECT id, links->>'wikidata' AS link
                FROM artists
                WHERE links->>'wikidata' IS NOT NULL
                ORDER BY name
                """;

            await using DbDataReader reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                string? qid = WikidataQid.FromUri(reader.GetString(1));

                if (qid is not null)
                {
                    map.TryAdd(qid, reader.GetGuid(0));
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return map;
    }

    /// <summary>
    /// Every influence edge already in the graph, as (from, to) keys. Read once and kept current in
    /// memory as batches land, so a re-run inserts nothing twice without a round trip per edge.
    /// </summary>
    private static async Task<HashSet<(Guid, Guid)>> LoadExistingEdgeKeysAsync(
        GrimoireDbContext db,
        CancellationToken ct)
    {
        var rows = await db.ArtistEdges
            .Where(e => e.Kind == EdgeKind.InfluencedBy)
            .Select(e => new { e.FromId, e.ToId })
            .ToListAsync(ct);

        return [.. rows.Select(e => (e.FromId, e.ToId))];
    }

    /// <summary>Inserts the edges of one batch that are not in the graph yet.</summary>
    private static async Task<(int Inserted, int Existing)> WriteAsync(
        GrimoireDbContext db,
        List<WikidataInfluence.Edge> edges,
        HashSet<(Guid, Guid)> known,
        CancellationToken ct)
    {
        List<WikidataInfluence.Edge> fresh = WikidataInfluence.NewEdges(edges, known);

        foreach (WikidataInfluence.Edge edge in fresh)
        {
            db.ArtistEdges.Add(new ArtistEdge
            {
                Id = Guid.NewGuid(),
                FromId = edge.FromId,
                ToId = edge.ToId,
                Kind = EdgeKind.InfluencedBy,
            });
        }

        if (fresh.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return (fresh.Count, edges.Count - fresh.Count);
    }
}
