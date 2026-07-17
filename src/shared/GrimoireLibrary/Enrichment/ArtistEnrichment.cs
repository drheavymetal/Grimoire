using Grimoire.Library.Services;

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
    /// Every clip this source matched to the artist, best first — including the one promoted to
    /// <see cref="PreviewUrl"/>, which is simply the first of these. Empty when the source had no
    /// audio, and empty for sources that are not about audio at all.
    /// <para>
    /// These cost nothing: the lookups already ask iTunes for 25 tracks and always did, and we kept
    /// one and dropped the rest (DECISIONS D67). Carrying them here rather than one URL is what lets
    /// a band be heard twice without asking anyone a second time.
    /// </para>
    /// </summary>
    public IReadOnlyList<PreviewCandidate> Previews { get; init; } = [];

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

    /// <summary>
    /// Genre tags the source resolved for this artist, most-relevant first, or empty when it has
    /// none. Filled by Last.fm alongside <see cref="Listeners"/> in the one <c>artist.getInfo</c>
    /// call. The job backfills them only where the artist has no tags yet, so the cleaner
    /// MusicBrainz tags are never overwritten (MEMORY §6b).
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
