using System.Text.Json.Serialization;

namespace Grimoire.Worker.MusicBrainz;

/// <summary>Envelope of the artist search endpoint.</summary>
public class ArtistSearchResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("artists")]
    public List<MbArtist> Artists { get; set; } = [];
}

/// <summary>An artist as returned by search or lookup.</summary>
public class MbArtist
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sort-name")]
    public string? SortName { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("life-span")]
    public MbLifeSpan? LifeSpan { get; set; }

    [JsonPropertyName("begin-area")]
    public MbArea? BeginArea { get; set; }

    [JsonPropertyName("area")]
    public MbArea? Area { get; set; }

    [JsonPropertyName("tags")]
    public List<MbTag>? Tags { get; set; }

    [JsonPropertyName("relations")]
    public List<MbRelation>? Relations { get; set; }
}

public class MbLifeSpan
{
    [JsonPropertyName("begin")]
    public string? Begin { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("ended")]
    public bool? Ended { get; set; }
}

public class MbArea
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class MbTag
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class MbRelation
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("begin")]
    public string? Begin { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("ended")]
    public bool? Ended { get; set; }

    [JsonPropertyName("attributes")]
    public List<string>? Attributes { get; set; }

    [JsonPropertyName("url")]
    public MbUrl? Url { get; set; }

    /// <summary>The artist on the other end of an artist-artist relation (e.g. a band member).</summary>
    [JsonPropertyName("artist")]
    public MbArtist? Artist { get; set; }
}

public class MbUrl
{
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }
}

/// <summary>Envelope of the release-group browse endpoint.</summary>
public class ReleaseGroupResponse
{
    [JsonPropertyName("release-group-count")]
    public int Count { get; set; }

    [JsonPropertyName("release-groups")]
    public List<MbReleaseGroup> ReleaseGroups { get; set; } = [];
}

public class MbReleaseGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; set; }

    [JsonPropertyName("secondary-types")]
    public List<string>? SecondaryTypes { get; set; }

    [JsonPropertyName("first-release-date")]
    public string? FirstReleaseDate { get; set; }
}
