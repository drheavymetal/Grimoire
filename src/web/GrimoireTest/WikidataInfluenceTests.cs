using Grimoire.Library.Wikidata;
using Xunit;

namespace Grimoire.Tests;

public class WikidataInfluenceTests
{
    private static SparqlResponse Response(params (string A, string B)[] rows)
    {
        SparqlResults results = new();

        foreach ((string a, string b) in rows)
        {
            results.Bindings.Add(new Dictionary<string, SparqlValue>
            {
                ["a"] = new SparqlValue { Type = "uri", Value = $"http://www.wikidata.org/entity/{a}" },
                ["b"] = new SparqlValue { Type = "uri", Value = $"http://www.wikidata.org/entity/{b}" },
            });
        }

        return new SparqlResponse { Results = results };
    }

    // --- Parse ---

    [Fact]
    public void Parse_ReadsInfluencedAndInfluencerQids()
    {
        SparqlResponse response = Response(("Q1", "Q2"), ("Q3", "Q4"));

        List<WikidataInfluence.Pair> pairs = WikidataInfluence.Parse(response);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(new WikidataInfluence.Pair("Q1", "Q2"), pairs[0]);
        Assert.Equal(new WikidataInfluence.Pair("Q3", "Q4"), pairs[1]);
    }

    [Fact]
    public void Parse_SkipsRowMissingAVariable()
    {
        SparqlResults results = new();
        results.Bindings.Add(new Dictionary<string, SparqlValue>
        {
            ["a"] = new SparqlValue { Value = "http://www.wikidata.org/entity/Q1" },
            // no "b"
        });

        List<WikidataInfluence.Pair> pairs = WikidataInfluence.Parse(new SparqlResponse { Results = results });

        Assert.Empty(pairs);
    }

    [Fact]
    public void Parse_NullResponse_ReturnsEmpty()
    {
        Assert.Empty(WikidataInfluence.Parse(null));
    }

    // --- ToEdges ---

    [Fact]
    public void ToEdges_KeepsOnlyPairsWithBothEndpointsInCorpus()
    {
        Guid darkthrone = Guid.NewGuid();
        Guid celticFrost = Guid.NewGuid();
        Dictionary<string, Guid> corpus = new()
        {
            ["Q1"] = darkthrone,
            ["Q2"] = celticFrost,
        };

        // (Q1 influenced_by Q2) resolves; (Q1 influenced_by Q99) does not — Q99 is not ours.
        List<WikidataInfluence.Pair> pairs =
        [
            new("Q1", "Q2"),
            new("Q1", "Q99"),
        ];

        List<WikidataInfluence.Edge> edges = WikidataInfluence.ToEdges(pairs, corpus);

        WikidataInfluence.Edge only = Assert.Single(edges);
        Assert.Equal(darkthrone, only.FromId);   // the influenced artist
        Assert.Equal(celticFrost, only.ToId);     // the influencer
    }

    [Fact]
    public void ToEdges_DropsSelfEdges()
    {
        Guid a = Guid.NewGuid();
        Dictionary<string, Guid> corpus = new() { ["Q1"] = a };

        List<WikidataInfluence.Edge> edges = WikidataInfluence.ToEdges([new("Q1", "Q1")], corpus);

        Assert.Empty(edges);
    }

    [Fact]
    public void ToEdges_DeduplicatesRepeatedPairs()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Dictionary<string, Guid> corpus = new() { ["Q1"] = a, ["Q2"] = b };

        List<WikidataInfluence.Edge> edges = WikidataInfluence.ToEdges(
            [new("Q1", "Q2"), new("Q1", "Q2")],
            corpus);

        Assert.Single(edges);
    }

    [Fact]
    public void ToEdges_UnmatchedInfluenced_IsDropped()
    {
        // The influencer is ours but the influenced is not — nothing to attach the edge to.
        Guid b = Guid.NewGuid();
        Dictionary<string, Guid> corpus = new() { ["Q2"] = b };

        Assert.Empty(WikidataInfluence.ToEdges([new("Q1", "Q2")], corpus));
    }

    [Fact]
    public void ToEdges_TargetOutsideTheCatalogue_IsAGapNotAnInventedNode()
    {
        // Measured against the live catalogue: of 1 245 distinct P737 targets, 759 are ours and 486
        // are not. Those 486 must vanish silently — an influence on a band we do not have is a fact
        // we cannot store, and inventing the node to hold it would be inventing catalogue
        // (Invariant 5). The band keeps the edges that DO resolve; it does not lose them.
        Guid mayhem = Guid.NewGuid();
        Guid venom = Guid.NewGuid();
        Dictionary<string, Guid> corpus = new() { ["Q1"] = mayhem, ["Q2"] = venom };

        List<WikidataInfluence.Edge> edges = WikidataInfluence.ToEdges(
            [new("Q1", "Q2"), new("Q1", "Q900"), new("Q1", "Q901")],
            corpus);

        WikidataInfluence.Edge only = Assert.Single(edges);
        Assert.Equal(new WikidataInfluence.Edge(mayhem, venom), only);
    }

    // --- NewEdges: idempotence above the database ---

    [Fact]
    public void NewEdges_EdgeAlreadyInTheGraph_IsNotProposedAgain()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        HashSet<(Guid, Guid)> known = [(a, b)];

        Assert.Empty(WikidataInfluence.NewEdges([new WikidataInfluence.Edge(a, b)], known));
    }

    [Fact]
    public void NewEdges_ReturnsOnlyTheUnknownOnes_AndLearnsThem()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        HashSet<(Guid, Guid)> known = [(a, b)];

        List<WikidataInfluence.Edge> fresh = WikidataInfluence.NewEdges(
            [new WikidataInfluence.Edge(a, b), new WikidataInfluence.Edge(a, c)],
            known);

        Assert.Equal([new WikidataInfluence.Edge(a, c)], fresh);

        // The new edge is now known, so the next batch of the same run cannot propose it either.
        Assert.Contains((a, c), known);
        Assert.Empty(WikidataInfluence.NewEdges([new WikidataInfluence.Edge(a, c)], known));
    }

    [Fact]
    public void NewEdges_ASecondSweepOfTheSameDataInsertsNothing()
    {
        // The pass has no marker table and does not need one: the whole catalogue is ~47 requests,
        // so it simply re-sweeps. That is only safe if a re-sweep is a no-op — re-running must not
        // duplicate a single edge.
        Guid darkthrone = Guid.NewGuid();
        Guid celticFrost = Guid.NewGuid();
        Guid bathory = Guid.NewGuid();
        Dictionary<string, Guid> corpus = new()
        {
            ["Q1"] = darkthrone,
            ["Q2"] = celticFrost,
            ["Q3"] = bathory,
        };
        List<WikidataInfluence.Pair> pairs = [new("Q1", "Q2"), new("Q1", "Q3")];

        // First run: an empty graph learns both edges.
        HashSet<(Guid, Guid)> graph = [];
        List<WikidataInfluence.Edge> first =
            WikidataInfluence.NewEdges(WikidataInfluence.ToEdges(pairs, corpus), graph);

        Assert.Equal(2, first.Count);

        // Second run, same Wikidata answer, graph reloaded from the database: nothing to insert.
        HashSet<(Guid, Guid)> reloaded = [.. graph];
        List<WikidataInfluence.Edge> second =
            WikidataInfluence.NewEdges(WikidataInfluence.ToEdges(pairs, corpus), reloaded);

        Assert.Empty(second);
        Assert.Equal(2, reloaded.Count);
    }

    // --- The two URI shapes that meet in this pass ---

    [Fact]
    public void QidsFromSparqlAndFromLinksColumn_MeetOnTheSameKey()
    {
        // The corpus map is keyed by the QID read out of artists.links['wikidata'], which
        // MusicBrainz stores as a wiki PAGE url; the pairs are keyed by the QID read out of a
        // SPARQL binding, which is an ENTITY uri. If those two ever stopped agreeing, every pair
        // would silently fail to resolve and the pass would write zero edges while reporting
        // thousands of pairs — so pin the round trip.
        string fromLinksColumn = WikidataQid.FromUri("https://www.wikidata.org/wiki/Q131113")!;
        Guid darkthrone = Guid.NewGuid();
        Guid celticFrost = Guid.NewGuid();
        Dictionary<string, Guid> corpus = new(StringComparer.Ordinal)
        {
            [fromLinksColumn] = darkthrone,
            ["Q504536"] = celticFrost,
        };

        // Q131113 influenced_by Q504536, straight off the SPARQL wire.
        List<WikidataInfluence.Pair> pairs = WikidataInfluence.Parse(Response(("Q131113", "Q504536")));
        List<WikidataInfluence.Edge> edges = WikidataInfluence.ToEdges(pairs, corpus);

        Assert.Equal([new WikidataInfluence.Edge(darkthrone, celticFrost)], edges);
    }
}
