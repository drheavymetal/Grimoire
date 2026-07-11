namespace Grimoire.Library.Wikidata;

/// <summary>
/// Turns a Wikidata P737 ("influenced by") SPARQL result into influence edges over our corpus
/// (feature B16). Pure and testable. The query binds <c>?a wdt:P737 ?b</c>, so each row reads
/// "<c>a</c> was influenced by <c>b</c>". An <see cref="InfluencedBy"/> edge is directed the same
/// way: <c>From</c> is the influenced artist, <c>To</c> is the influencer. Only pairs whose two
/// endpoints are <b>both</b> in our corpus become edges — anything that does not resolve is
/// dropped, never invented as a new node (autonomous-mode rule).
/// </summary>
public static class WikidataInfluence
{
    /// <summary>One influence fact as QIDs, before it is resolved against the corpus.</summary>
    /// <param name="InfluencedQid">The artist that was influenced (SPARQL variable <c>a</c>).</param>
    /// <param name="InfluencerQid">The artist that influenced them (variable <c>b</c>).</param>
    public readonly record struct Pair(string InfluencedQid, string InfluencerQid);

    /// <summary>A resolved influence edge: <c>From</c> influenced_by <c>To</c>.</summary>
    public readonly record struct Edge(Guid FromId, Guid ToId);

    /// <summary>
    /// Reads the (influenced, influencer) QID pairs from a SPARQL response. Rows missing either
    /// variable, or whose value is not a well-formed QID, are skipped.
    /// </summary>
    public static List<Pair> Parse(SparqlResponse? response, string influencedVar = "a", string influencerVar = "b")
    {
        List<Pair> pairs = [];

        if (response?.Results?.Bindings is null)
        {
            return pairs;
        }

        foreach (Dictionary<string, SparqlValue> row in response.Results.Bindings)
        {
            string? influenced = row.TryGetValue(influencedVar, out SparqlValue? a) ? WikidataQid.FromUri(a.Value) : null;
            string? influencer = row.TryGetValue(influencerVar, out SparqlValue? b) ? WikidataQid.FromUri(b.Value) : null;

            if (influenced is null || influencer is null)
            {
                continue;
            }

            pairs.Add(new Pair(influenced, influencer));
        }

        return pairs;
    }

    /// <summary>
    /// Maps QID pairs to corpus edges, keeping only pairs whose <b>both</b> endpoints resolve to
    /// an artist in <paramref name="qidToArtist"/>. Self-edges (an artist influencing itself) and
    /// duplicate (from, to) pairs are dropped. Order is deterministic (first-seen wins).
    /// </summary>
    public static List<Edge> ToEdges(IEnumerable<Pair> pairs, IReadOnlyDictionary<string, Guid> qidToArtist)
    {
        List<Edge> edges = [];
        HashSet<(Guid, Guid)> seen = [];

        foreach (Pair pair in pairs)
        {
            if (!qidToArtist.TryGetValue(pair.InfluencedQid, out Guid fromId)
                || !qidToArtist.TryGetValue(pair.InfluencerQid, out Guid toId))
            {
                continue;
            }

            if (fromId == toId)
            {
                continue;
            }

            if (seen.Add((fromId, toId)))
            {
                edges.Add(new Edge(fromId, toId));
            }
        }

        return edges;
    }
}
