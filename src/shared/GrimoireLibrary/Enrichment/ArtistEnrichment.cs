namespace Grimoire.Library.Enrichment;

/// <summary>
/// What an <see cref="IEnrichmentSource"/> returns for one artist. Every field is
/// optional: a source contributes only what it actually has. The orchestrator merges
/// these additively across sources and never overwrites a present value with null.
/// </summary>
public sealed class ArtistEnrichment
{
    /// <summary>
    /// A 30–45 s preview URL to serve in The Rite, or null when the source has no audio
    /// for this artist. Null is a real gap (the band is inaudible), not a failure.
    /// </summary>
    public string? PreviewUrl { get; init; }

    /// <summary>
    /// Last.fm listener count for this artist, or null when the source could not resolve it
    /// to the right band. Null is a real gap (DECISIONS D25: better a missing count than the
    /// wrong band's), never invented — and a null listener count derives a null rank.
    /// </summary>
    public int? Listeners { get; init; }

    /// <summary>
    /// Curated streaming links to add to <c>artists.links</c>, keyed by service. Keys are
    /// namespaced with the <c>listen:</c> prefix (see <see cref="Services.StreamingLinks"/>)
    /// so they never collide with the raw MusicBrainz url-rels already stored there.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; init; }
        = new Dictionary<string, string>();
}
