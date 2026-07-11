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
}
