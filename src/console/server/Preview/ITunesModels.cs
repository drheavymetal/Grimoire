using System.Text.Json.Serialization;

namespace Grimoire.Worker.Preview;

/// <summary>Envelope of the iTunes Search API.</summary>
public sealed class ITunesSearchResponse
{
    [JsonPropertyName("resultCount")]
    public int ResultCount { get; set; }

    [JsonPropertyName("results")]
    public List<ITunesResult> Results { get; set; } = [];
}

public sealed class ITunesResult
{
    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("previewUrl")]
    public string? PreviewUrl { get; set; }

    /// <summary>Apple Music artist page — the exact link kept in artists.links (D10).</summary>
    [JsonPropertyName("artistViewUrl")]
    public string? ArtistViewUrl { get; set; }
}
