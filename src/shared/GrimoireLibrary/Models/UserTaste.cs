using Pgvector;

namespace Grimoire.Library.Models;

/// <summary>
/// A user's taste vector and repulsion vector for the discovery engine (The Rite). Both are
/// CENTRED vectors (DECISIONS D26): they are assembled by averaging stored artist embeddings,
/// which the ETL already centred, so the corpus mean is never subtracted from them again.
/// <see cref="Embedding"/> is the moving average of what the user summons; <see cref="Repulsion"/>
/// the moving average of what they banish, which the ring search actively subtracts (D4).
/// One row per user (<see cref="UserId"/> is the key).
/// </summary>
public class UserTaste
{
    public Guid UserId { get; set; }

    public Vector? Embedding { get; set; }

    public Vector? Repulsion { get; set; }

    public int DepthScore { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
