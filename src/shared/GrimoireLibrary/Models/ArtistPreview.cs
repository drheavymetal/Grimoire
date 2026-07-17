namespace Grimoire.Library.Models;

/// <summary>
/// One 30–45 s audio preview of one artist: the URL, which source stands behind it, and the track it
/// is a clip of. Several of these per artist — the alternates to <see cref="Artist.PreviewUrl"/>,
/// which stays exactly what it always was: the one clip The Rite serves.
///
/// <para>
/// <b>Why this table exists at all.</b> Nothing about the sources limited us to one clip: iTunes has
/// always been asked for <c>limit=25</c> and we kept the first match and dropped the other 24
/// (DECISIONS D67). The single column was a storage decision that never had a reason to be otherwise,
/// because The Rite only ever needs one cut. "Guess the band" needs a second: played over your own
/// grimoire, a one-clip band can only ever replay the exact audio you already heard when you summoned
/// it, which measures memory of that clip rather than knowledge of the band. So these rows are
/// <b>additive</b> — they are what was already paid for and thrown away.
/// </para>
/// <para>
/// <b>Why a child table and not more columns on <see cref="Artist"/>.</b> The same two scars that
/// shaped <see cref="ArtistBiography"/>. (1) An artist row carries a 768-dimension embedding under an
/// HNSW index, so writing one is the expensive, index-churning UPDATE that took production down when a
/// migration did it in bulk (MEMORY §6f); INSERTs into a light table touch none of that. (2) A
/// <c>preview_url_2</c>/<c>preview_url_3</c> pair answers "two" and then "three" and each further clip
/// is another migration on the catalogue's hottest table, while a jsonb list could not be filtered in
/// SQL — the anti-join that finds the artists still owing a harvest would have to materialise the
/// catalogue to read one key, which is the exact bug D61 found in <c>ListenersJob</c>.
/// </para>
/// <para>
/// <b>Still no audio, still no player.</b> Only URLs are stored, never bytes (DECISIONS D40/D10), and
/// the audio still streams through the capability proxy that hides the origin (D32). A handful of
/// alternate clips is not a track index and not a player: <see cref="Services.ArtistPreviews"/> caps
/// how many are kept per artist for exactly that reason (Invariant 4).
/// </para>
/// </summary>
public class ArtistPreview
{
    public Guid ArtistId { get; set; }

    /// <summary>
    /// The preview URL. Half of the composite primary key with <see cref="ArtistId"/>, because the URL
    /// <em>is</em> the identity of a clip: keying on it makes a repeated pass a find-or-insert instead
    /// of a duplicate, without a surrogate id nobody would ever look a row up by. It is not keyed on
    /// (source, title) instead, because a title is nullable and the same title recurs across releases —
    /// that key would collapse two real clips into one and reject the second.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Which source produced this clip — the <c>IEnrichmentSource.Name</c> that returned it ("iTunes",
    /// "Deezer"). Stored rather than re-derived from the hostname because R9 is a live risk and its one
    /// cheap mitigation is attribution at the reveal ("provided courtesy of iTunes", plus a link to the
    /// store): you cannot attribute a clip you cannot name the origin of. <c>artists.preview_url</c>
    /// never recorded this, which is why the mitigation was not buildable from the data we had.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// The track this is a clip of, as the source titled it, or null when the source gave no title.
    /// Null is a real gap, never invented (Invariant 5).
    /// <para>
    /// It earns its column three times over: it is the other half of the R9 attribution (a clip is
    /// credited as a track, not as an anonymous noise); it is the only way to tell "a different track"
    /// from "the same song under a second URL", which the two sources routinely both return, and
    /// serving that as a new clip would be a silent lie about the game being a game; and it is what
    /// lets a round end by naming what you just heard.
    /// </para>
    /// <para>
    /// <b>Hazard for anything that shows it: a title leaks the band.</b> "Iron Maiden" is a track by
    /// Iron Maiden. Never render this before the answer is in.
    /// </para>
    /// </summary>
    public string? TrackTitle { get; set; }

    /// <summary>
    /// When this clip was collected. Preview URLs do expire and nothing refreshes them yet (D40: a
    /// stale URL 404s at the proxy and degrades to an empty state); this is what a refresh pass would
    /// have to sort by, and what tells an operator whether a band's clips predate a change to the
    /// harvest.
    /// </summary>
    public DateTime CollectedAt { get; set; }
}
