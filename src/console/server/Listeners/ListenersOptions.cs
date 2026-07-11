namespace Grimoire.Worker.Listeners;

/// <summary>Controls one resumable, batched Last.fm listeners run.</summary>
public sealed class ListenersOptions
{
    /// <summary>Maximum number of not-yet-populated artists to process this run.</summary>
    public int Limit { get; set; } = 500;
}
