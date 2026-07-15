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
}
