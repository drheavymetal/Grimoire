using Grimoire.Library.Enrichment;

namespace Grimoire.Library.Wikidata;

/// <summary>
/// The result of one SPARQL batch: what happened, and the rows if any came back. Classification
/// rules live in <see cref="WikidataOutcome"/>.
/// </summary>
/// <param name="Outcome">Matched (rows), NoData (an empty answer), Unavailable (no answer).</param>
/// <param name="Response">The parsed result set; non-null only when the endpoint answered.</param>
public readonly record struct WikidataQueryResult(EnrichmentOutcome Outcome, SparqlResponse? Response)
{
    /// <summary>
    /// Wikidata answered — with rows or without. Whatever it said is this batch's truth, and the
    /// batch is done. When this is false the batch simply has not happened yet: nothing about it
    /// may be written down, and a later run must sweep it again.
    /// </summary>
    public bool Answered => Outcome != EnrichmentOutcome.Unavailable;

    /// <summary>No usable answer: a non-success status, a timeout, a dropped connection, bad JSON.</summary>
    public static WikidataQueryResult Unavailable { get; } = new(EnrichmentOutcome.Unavailable, null);

    /// <summary>Classifies a parsed body into a result.</summary>
    public static WikidataQueryResult FromBody(SparqlResponse? parsed) =>
        new(WikidataOutcome.FromBody(parsed), parsed);
}
