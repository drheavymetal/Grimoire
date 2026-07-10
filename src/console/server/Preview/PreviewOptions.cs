namespace Grimoire.Worker.Preview;

/// <summary>Controls one lazy, batched preview-resolution run.</summary>
public sealed class PreviewOptions
{
    /// <summary>Maximum number of not-yet-attempted artists to process this run.</summary>
    public int Limit { get; set; } = 60;
}
