namespace Grimoire.Library.Enrichment;

/// <summary>
/// What an <see cref="IEnrichmentSource"/> returns for one artist: the outcome, plus the data
/// when there is any. <see cref="Enrichment"/> is non-null only for
/// <see cref="EnrichmentOutcome.Matched"/>; the other two outcomes carry nothing by construction,
/// which is the point — a caller cannot read data out of a failure.
/// </summary>
public readonly record struct EnrichmentResult(EnrichmentOutcome Outcome, ArtistEnrichment? Enrichment)
{
    /// <summary>The source had data: stamp it checked and keep the payload.</summary>
    public static EnrichmentResult Matched(ArtistEnrichment enrichment) =>
        new(EnrichmentOutcome.Matched, enrichment);

    /// <summary>A definitive "nothing here": stamp it checked, store nothing.</summary>
    public static EnrichmentResult NoData { get; } = new(EnrichmentOutcome.NoData, null);

    /// <summary>No answer at all: leave the artist unstamped so a later run retries it.</summary>
    public static EnrichmentResult Unavailable { get; } = new(EnrichmentOutcome.Unavailable, null);
}
