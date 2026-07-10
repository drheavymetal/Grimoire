namespace Grimoire.Worker.Embedding;

/// <summary>Controls the embedding pass.</summary>
public sealed class EmbeddingOptions
{
    /// <summary>Feature flag (Invariant 5 / D9). When false the pass does nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Expected embedding dimensionality (nomic-embed-text = 768).</summary>
    public int Dimensions { get; set; } = 768;
}
