using System.Text.Json.Serialization;

namespace Grimoire.Library.Wikidata;

/// <summary>
/// The shape of a Wikidata SPARQL JSON result set (<c>application/sparql-results+json</c>).
/// Only the part we read is modelled: the list of result rows, each a map from variable name
/// to a value object. Parsing into domain facts lives in <see cref="WikidataInfluence"/> and
/// <see cref="WikidataDeaths"/> so it can be tested without a network.
/// </summary>
public sealed class SparqlResponse
{
    [JsonPropertyName("results")]
    public SparqlResults? Results { get; set; }
}

public sealed class SparqlResults
{
    [JsonPropertyName("bindings")]
    public List<Dictionary<string, SparqlValue>> Bindings { get; set; } = [];
}

public sealed class SparqlValue
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
