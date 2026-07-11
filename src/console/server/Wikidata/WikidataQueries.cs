using Grimoire.Library.Wikidata;

namespace Grimoire.Worker.Wikidata;

/// <summary>
/// Builds the batched SPARQL queries the worker sends. Each query pins the subject to a
/// <c>VALUES</c> list of our own QIDs, so Wikidata only ever computes over our corpus, never the
/// whole graph. The object side is left free and filtered against the corpus afterwards in code
/// (<see cref="WikidataInfluence.ToEdges"/>).
/// </summary>
public static class WikidataQueries
{
    /// <summary>
    /// P737 ("influenced by"): for each of our artists <c>?a</c>, the entities <c>?b</c> that
    /// influenced them. The caller keeps only pairs whose <c>?b</c> is also in the corpus.
    /// </summary>
    public static string Influence(IEnumerable<string> qids)
    {
        string values = Values(qids);

        return $$"""
            SELECT ?a ?b WHERE {
              VALUES ?a { {{values}} }
              ?a wdt:P737 ?b .
            }
            """;
    }

    /// <summary>
    /// P570 (date of death) with optional P20 (place of death), for our people. The label
    /// service resolves the place QID to an English label bound as <c>?placeLabel</c>.
    /// </summary>
    public static string Deaths(IEnumerable<string> qids)
    {
        string values = Values(qids);

        return $$"""
            SELECT ?a ?death ?placeLabel WHERE {
              VALUES ?a { {{values}} }
              ?a wdt:P570 ?death .
              OPTIONAL { ?a wdt:P20 ?place . }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            """;
    }

    private static string Values(IEnumerable<string> qids)
    {
        return string.Join(' ', qids.Select(WikidataQid.ToPrefixed));
    }
}
