using Pgvector;

namespace Grimoire.Library.Models;

/// <summary>
/// A versioned snapshot of a user's taste vector (feature C16, "your trajectory"). One row is
/// written each time the taste changes in a relevant way — a cold-start seed and every summon —
/// so the sequence, read in <see cref="CreatedAt"/> order, is the path the taste travelled.
///
/// <para>
/// The <see cref="Embedding"/> is already CENTRED (DECISIONS D26), like every taste vector: it is
/// a copy of <c>user_taste.embedding</c> at the moment of the snapshot, never re-centred. It is
/// projected onto the Atlas plane at read time (the same PCA basis the stars use) to draw the path,
/// and consecutive cosine distances give the drift metric.
/// </para>
///
/// <para>
/// EXPOSURE (declared, DECISIONS D28 style): this table retains the full history of a user's taste
/// embeddings indefinitely. It is more revealing than <c>user_taste</c>, which keeps only the
/// latest vector — the trajectory exposes how a user's taste moved over time. Rows cascade-delete
/// with the account. No pruning/TTL is implemented; a retention policy is a deliberate later choice.
/// </para>
/// </summary>
public class TasteSnapshot
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Vector? Embedding { get; set; }

    public int DepthScore { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
