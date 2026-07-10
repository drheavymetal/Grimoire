namespace Grimoire.Worker;

/// <summary>
/// Controls the seed run. The job does nothing unless <see cref="RunSeed"/> is set,
/// so a plain host start never fetches data.
/// </summary>
public class SeedOptions
{
    /// <summary>True when the process was invoked with the "seed" verb.</summary>
    public bool RunSeed { get; set; }

    /// <summary>Approximate number of tag-matched artists to gather (excludes anchors).</summary>
    public int Target { get; set; } = 300;
}
