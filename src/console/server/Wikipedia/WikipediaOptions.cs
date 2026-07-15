namespace Grimoire.Worker.Wikipedia;

/// <summary>Controls one resumable, batched Wikipedia biography pass.</summary>
public sealed class WikipediaOptions
{
    /// <summary>Maximum number of not-yet-checked artists to process this run.</summary>
    public int Limit { get; set; } = 500;
}
