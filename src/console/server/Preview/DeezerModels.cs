using System.Text.Json.Serialization;

namespace Grimoire.Worker.Preview;

/// <summary>Envelope of a Deezer search/list endpoint.</summary>
public sealed class DeezerListResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = [];
}

public sealed class DeezerArtist
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Exact Deezer artist page — the exact link kept in artists.links (D10).</summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }
}

public sealed class DeezerTrack
{
    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    /// <summary>The track this preview is a clip of. Stored per clip (DECISIONS D67 / ArtistPreview).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
