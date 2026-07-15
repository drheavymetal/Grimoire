namespace Grimoire.Library.Models;

/// <summary>
/// An explicit taste anchor: a band the user has deliberately pinned as representative of what
/// they like (the HYBRID taste model, Pedro's choice). Anchors are a NEW concept, SEPARATE from
/// summons — the Rite keeps learning its EMA untouched, while the anchor set is the editable seed
/// the user curates by hand. "Rebuild taste from anchors" re-seeds <c>user_taste.embedding</c> with
/// the mean of the anchors' already-CENTRED embeddings (DECISIONS D26) via
/// <see cref="Services.TasteMath.Seed"/>.
///
/// <para>
/// One row per (user, band): the composite key (<see cref="UserId"/>, <see cref="ArtistId"/>) makes
/// adding an anchor idempotent and removing it a plain delete. Rows cascade-delete with either the
/// account or the artist.
/// </para>
/// </summary>
public class TasteAnchor
{
    public Guid UserId { get; set; }

    public Guid ArtistId { get; set; }

    public DateTime AddedAt { get; set; }
}
