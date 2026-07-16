using Grimoire.Library.Services;

namespace Grimoire.Worker.Wikipedia;

/// <summary>Controls one resumable, batched Wikipedia biography pass.</summary>
public sealed class WikipediaOptions
{
    /// <summary>Maximum number of not-yet-checked artists to process this run.</summary>
    public int Limit { get; set; } = 500;

    /// <summary>
    /// How many MBIDs go into a single Wikidata SPARQL <c>VALUES</c> query. Larger means fewer WDQS
    /// round trips (the throughput win) but a heavier query; ~50 stays well within WDQS limits.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// The Wikipedia editions to resolve, as bare language codes. Every one is asked for in the
    /// <b>same</b> SPARQL query, so a language costs sitelink rows, not round trips — the expense is
    /// one REST summary call per article actually found.
    /// <para>
    /// This is the knob the whole design exists to make cheap. The gap it addresses is measured:
    /// inside the discoverable pool only 8.3% of Nameless bands and 17.5% of Forgotten ones have a
    /// biography, and that underground is Nordic and German — the article often exists, just not in
    /// English. Adding <c>no</c>, <c>sv</c>, <c>fi</c> or <c>de</c> here is the whole change: no
    /// schema, no code, no migration.
    /// </para>
    /// <para>
    /// <c>en</c> is a language like any other to the pass, but its result is stored on the artist row
    /// rather than the child table, because that text feeds the embedding (see
    /// <see cref="Grimoire.Library.Models.ArtistBiography"/>). Dropping <c>en</c> from this list stops
    /// English being refreshed; it does not delete anything.
    /// </para>
    /// </summary>
    public string[] Languages { get; set; } = [ArtistBiographies.English, "es"];
}
