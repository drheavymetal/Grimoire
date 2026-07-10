using Pgvector;

namespace Grimoire.Library.Models;

/// <summary>
/// A user's taste vector and repulsion vector for the discovery engine. Model
/// reserved; no table is created in this pass (nothing writes taste yet).
/// </summary>
public class UserTaste
{
    public Guid UserId { get; set; }

    public Vector? Embedding { get; set; }

    public Vector? Repulsion { get; set; }

    public int DepthScore { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
