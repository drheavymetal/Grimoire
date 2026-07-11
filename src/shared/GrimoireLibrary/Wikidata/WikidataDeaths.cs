using System.Globalization;

namespace Grimoire.Library.Wikidata;

/// <summary>
/// Turns a Wikidata P570 (date of death) / P20 (place of death) SPARQL result into death facts
/// for our people (feature C12, In Memoriam). Pure and testable. Only what Wikidata asserts is
/// kept; a missing date or place stays null, never invented. The date literal is an ISO
/// timestamp (e.g. <c>1993-12-08T00:00:00Z</c>), possibly at reduced precision (year-only dates
/// come through as <c>YYYY-01-01</c>); the date portion is parsed and the time discarded.
/// </summary>
public static class WikidataDeaths
{
    /// <summary>A resolved death fact for one person, keyed by QID.</summary>
    /// <param name="Qid">Wikidata item id of the person.</param>
    /// <param name="Date">Date of death (P570), or null if unparseable.</param>
    /// <param name="Place">Place-of-death label (P20), or null if not asserted.</param>
    public readonly record struct Death(string Qid, DateOnly? Date, string? Place);

    /// <summary>
    /// Reads death facts from a SPARQL response. Rows whose subject is not a well-formed QID are
    /// skipped. A row with a QID but an unparseable date yields a <see cref="Death"/> with a null
    /// date (still useful if it carries a place).
    /// </summary>
    public static List<Death> Parse(
        SparqlResponse? response,
        string subjectVar = "a",
        string deathVar = "death",
        string placeVar = "placeLabel")
    {
        List<Death> deaths = [];

        if (response?.Results?.Bindings is null)
        {
            return deaths;
        }

        foreach (Dictionary<string, SparqlValue> row in response.Results.Bindings)
        {
            string? qid = row.TryGetValue(subjectVar, out SparqlValue? a) ? WikidataQid.FromUri(a.Value) : null;

            if (qid is null)
            {
                continue;
            }

            DateOnly? date = row.TryGetValue(deathVar, out SparqlValue? d) ? ParseDate(d.Value) : null;
            string? place = row.TryGetValue(placeVar, out SparqlValue? p) && !string.IsNullOrWhiteSpace(p.Value)
                ? p.Value
                : null;

            deaths.Add(new Death(qid, date, place));
        }

        return deaths;
    }

    /// <summary>
    /// Parses the date portion of a Wikidata ISO timestamp into a <see cref="DateOnly"/>.
    /// Returns null when the value is empty or not a positive-year <c>YYYY-MM-DD</c> date
    /// (BCE / reduced-precision oddities that do not apply to modern musicians are dropped).
    /// </summary>
    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        int t = value.IndexOf('T', StringComparison.Ordinal);
        string datePart = t >= 0 ? value[..t] : value;

        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
            ? parsed
            : null;
    }
}
