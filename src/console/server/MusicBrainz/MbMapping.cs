using Grimoire.Library.Models;

namespace Grimoire.Worker.MusicBrainz;

/// <summary>Shared mappings from MusicBrainz vocabulary to the domain model.</summary>
public static class MbMapping
{
    /// <summary>Maps a MusicBrainz artist type to <see cref="ArtistKind"/>, defaulting to Group.</summary>
    public static ArtistKind MapKind(string? type)
    {
        return type switch
        {
            "Person" => ArtistKind.Person,
            "Orchestra" => ArtistKind.Orchestra,
            "Choir" => ArtistKind.Choir,
            _ => ArtistKind.Group,
        };
    }
}
