namespace Grimoire.Worker.MetalArchives;

/// <summary>Controls one resumable, batched Metal Archives pass.</summary>
public sealed class MetalArchivesOptions
{
    /// <summary>Maximum number of not-yet-checked bands to process this run.</summary>
    public int Limit { get; set; } = 500;
}
