using System.Net;
using Grimoire.Library.Enrichment;

namespace Grimoire.Library.Wikidata;

/// <summary>
/// How one SPARQL batch against the Wikidata Query Service resolved, in the vocabulary the
/// enrichment sources share (<see cref="EnrichmentOutcome"/>). Pure and side-effect free so the
/// rules live under test rather than inside an HTTP client no test can reach.
/// <para>
/// The distinction that earns its keep is <see cref="EnrichmentOutcome.NoData"/> versus
/// <see cref="EnrichmentOutcome.Unavailable"/>: an empty result set is Wikidata answering "none of
/// these items carries that property", while Unavailable is Wikidata not answering at all.
/// Collapsing the two into a bare <c>null</c> is what let the influence pass sweep the whole
/// catalogue, drop every batch it failed to fetch, and still report success (D61, MEMORY §6f).
/// </para>
/// </summary>
public static class WikidataOutcome
{
    /// <summary>
    /// How a non-success status from WDQS resolves: <b>always</b>
    /// <see cref="EnrichmentOutcome.Unavailable"/>.
    /// <para>
    /// This is the deliberate asymmetry with <see cref="HttpOutcome.IsTransient"/>, the
    /// catalogue-wide rule that a 4xx is a definitive verdict worth stamping (D61). That rule is
    /// right for a source queried one artist at a time — a 404 from Wikipedia is a fact about
    /// <em>that</em> title. It is wrong here: a batch query names a thousand artists at once, so a
    /// 4xx from WDQS is a verdict on <b>our query</b> — a 400 for bad SPARQL, a 414 for a batch too
    /// long for the URL — and it would greet every batch alike. Treating it as an answer would write
    /// a bug in our own code across the entire catalogue as "these bands influenced nobody".
    /// A pass that spins is a loud failure; a pass that records a lie is a silent one. Prefer loud.
    /// </para>
    /// </summary>
    public static EnrichmentOutcome FromFailedStatus(HttpStatusCode status)
    {
        // The status is not consulted, and that is the point: no reading of it may reach a
        // conclusion about the artists in the batch. It stays in the signature because callers do
        // use it — to decide how loudly to log — and because the day someone reaches for
        // HttpOutcome.IsTransient here, this method is where the tests will stop them.
        _ = status;

        return EnrichmentOutcome.Unavailable;
    }

    /// <summary>
    /// How a parsed body resolves: <see cref="EnrichmentOutcome.Matched"/> when the result set has
    /// rows, <see cref="EnrichmentOutcome.NoData"/> when WDQS answered with an <em>empty</em> result
    /// set — a real, definitive "none of these", and what most batches of a catalogue sweep look
    /// like — and <see cref="EnrichmentOutcome.Unavailable"/> when there is no result set to read.
    /// <para>
    /// A body carrying no result set at all is not "nothing here": a SPARQL 200 always has one, so
    /// its absence means we are holding something that is not a result set. That is no answer, and
    /// it is classified as such rather than quietly counted as an empty one.
    /// </para>
    /// </summary>
    public static EnrichmentOutcome FromBody(SparqlResponse? parsed)
    {
        if (parsed?.Results is null)
        {
            return EnrichmentOutcome.Unavailable;
        }

        return parsed.Results.Bindings is { Count: > 0 }
            ? EnrichmentOutcome.Matched
            : EnrichmentOutcome.NoData;
    }
}
