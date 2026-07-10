namespace Grimoire.Library.Models;

/// <summary>
/// A record label.
/// </summary>
public class Label
{
    public Guid Id { get; set; }

    public Guid Mbid { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Country { get; set; }

    public List<Release> Releases { get; set; } = [];
}
