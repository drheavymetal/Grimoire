using Pgvector;

namespace Grimoire.Library.Models;

/// <summary>
/// Corpus-level statistics for the discovery engine, held as a single row. The one field
/// that matters is <see cref="MeanEmbedding"/>: the mean of every artist embedding, which
/// the ETL subtracts before indexing (DECISIONS D26, variant C). It is persisted here so
/// the query side can subtract the very same mean from the user's taste vector — percentiles
/// toward the user, radii toward the index. Without the stored mean the query vector and the
/// indexed vectors would live in different frames and the ring search would be meaningless.
/// </summary>
public class CorpusStat
{
    /// <summary>Fixed single-row key. Always <see cref="SingletonId"/>.</summary>
    public int Id { get; set; }

    /// <summary>The single row's id. There is only ever one corpus-stats row.</summary>
    public const int SingletonId = 1;

    /// <summary>Mean of all non-null artist embeddings (nomic-embed-text, 768 dims).</summary>
    public Vector? MeanEmbedding { get; set; }

    /// <summary>How many artist embeddings the mean was computed over.</summary>
    public int ArtistCount { get; set; }

    /// <summary>When the mean was last recomputed.</summary>
    public DateTimeOffset ComputedAt { get; set; }
}
