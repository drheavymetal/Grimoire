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

    /// <summary>
    /// Maps an artist's url-rels into the links dictionary (keyed by relation type, e.g.
    /// <c>wikidata</c>, <c>discogs</c>, <c>official homepage</c>). Returns null when there are
    /// no usable links, so a caller can leave the column null rather than write an empty object.
    /// </summary>
    public static Dictionary<string, string>? MapLinks(List<MbRelation>? relations)
    {
        if (relations is null)
        {
            return null;
        }

        Dictionary<string, string> links = new(StringComparer.OrdinalIgnoreCase);

        foreach (MbRelation relation in relations)
        {
            string? resource = relation.Url?.Resource;

            if (!string.IsNullOrWhiteSpace(relation.Type) && !string.IsNullOrWhiteSpace(resource))
            {
                links[relation.Type] = resource;
            }
        }

        return links.Count == 0 ? null : links;
    }
}
