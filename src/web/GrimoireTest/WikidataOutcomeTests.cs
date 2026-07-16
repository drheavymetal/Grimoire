using System.Net;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Wikidata;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The rule that decides whether a SPARQL batch happened. Getting it wrong is what left the
/// influence pillar with 69 edges: a failed batch resolved to a bare null, parsed into an empty
/// list, and became indistinguishable from Wikidata saying "none of these bands has an influence"
/// — so a sweep that fetched nothing reported success (D61, MEMORY §6f).
/// </summary>
public class WikidataOutcomeTests
{
    private static SparqlResponse WithRows(int count)
    {
        SparqlResults results = new();

        for (int i = 0; i < count; i++)
        {
            results.Bindings.Add(new Dictionary<string, SparqlValue>
            {
                ["a"] = new SparqlValue { Value = $"http://www.wikidata.org/entity/Q{i + 1}" },
                ["b"] = new SparqlValue { Value = "http://www.wikidata.org/entity/Q100" },
            });
        }

        return new SparqlResponse { Results = results };
    }

    // --- The WDQS asymmetry: no status is ever an answer about the artists ---

    [Theory]
    // Transient by the catalogue-wide rule, and Unavailable here too.
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    // DEFINITIVE by the catalogue-wide rule (HttpOutcome.IsTransient says false) — and still
    // Unavailable here. This is the whole asymmetry: these are verdicts on OUR query, not on the
    // thousand artists named in it. A 414 is the batch outgrowing the URL; a 400 is bad SPARQL; a
    // 403 is a rejected User-Agent. Each arrives for every batch alike, so honouring it as an
    // answer would stamp a bug in our own code across the entire catalogue.
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestUriTooLong)]
    public void FromFailedStatus_IsAlwaysUnavailable(HttpStatusCode status)
    {
        Assert.Equal(EnrichmentOutcome.Unavailable, WikidataOutcome.FromFailedStatus(status));
    }

    [Fact]
    public void FromFailedStatus_NeverAgreesWithTheCatalogueWideRuleOn4xx()
    {
        // Pinned deliberately: the day someone "tidies up" by routing WDQS through
        // HttpOutcome.IsTransient, these two lines stop disagreeing and this test fails.
        Assert.False(HttpOutcome.IsTransient(HttpStatusCode.RequestUriTooLong));
        Assert.Equal(
            EnrichmentOutcome.Unavailable,
            WikidataOutcome.FromFailedStatus(HttpStatusCode.RequestUriTooLong));
    }

    // --- An empty answer is an answer; no answer is not ---

    [Fact]
    public void FromBody_WithRows_IsMatched()
    {
        Assert.Equal(EnrichmentOutcome.Matched, WikidataOutcome.FromBody(WithRows(3)));
    }

    [Fact]
    public void FromBody_EmptyResultSet_IsNoData()
    {
        // WDQS answered: none of the QIDs in this batch carries P737. A real, definitive gap — and
        // NOT a failure. Most batches of a 46k-QID sweep look exactly like this.
        Assert.Equal(EnrichmentOutcome.NoData, WikidataOutcome.FromBody(WithRows(0)));
    }

    [Fact]
    public void FromBody_NoBody_IsUnavailable()
    {
        Assert.Equal(EnrichmentOutcome.Unavailable, WikidataOutcome.FromBody(null));
    }

    [Fact]
    public void FromBody_NoResultSet_IsUnavailable()
    {
        // A 200 whose body carries no result set at all is not "nothing here" — it is a body that
        // is not a SPARQL answer, which is the same as not having been answered.
        Assert.Equal(EnrichmentOutcome.Unavailable, WikidataOutcome.FromBody(new SparqlResponse()));
    }

    // --- The distinction the callers actually branch on ---

    [Fact]
    public void Answered_SeparatesAnEmptyAnswerFromNoAnswer()
    {
        Assert.True(WikidataQueryResult.FromBody(WithRows(0)).Answered);
        Assert.True(WikidataQueryResult.FromBody(WithRows(2)).Answered);
        Assert.False(WikidataQueryResult.Unavailable.Answered);
    }

    [Fact]
    public void Unavailable_CarriesNoRows()
    {
        // A caller must not be able to read data out of a failure.
        Assert.Null(WikidataQueryResult.Unavailable.Response);
    }
}
