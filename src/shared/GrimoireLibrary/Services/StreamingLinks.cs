namespace Grimoire.Library.Services;

/// <summary>
/// Builds the curated streaming links stored in <c>artists.links</c> (DECISIONS D10 /
/// SPEC B26). Apple Music and Deezer are exact when resolved in the ETL; the rest are
/// search URLs built from the artist name. Grimoire never plays music (Invariant 4): these
/// are outbound links surfaced only after The Rite's reveal, resolved once, with zero hot
/// calls. Keys are namespaced with the <c>listen:</c> prefix so they never overwrite the
/// raw MusicBrainz url-rels (discogs, wikidata, youtube channel…) already in the column.
/// </summary>
public static class StreamingLinks
{
    /// <summary>Prefix that marks a curated streaming link, distinguishing it from MB url-rels.</summary>
    public const string Prefix = "listen:";

    public const string AppleMusicKey = Prefix + "apple_music";
    public const string DeezerKey = Prefix + "deezer";
    public const string SpotifyKey = Prefix + "spotify";
    public const string YouTubeMusicKey = Prefix + "youtube_music";
    public const string YouTubeKey = Prefix + "youtube";
    public const string TidalKey = Prefix + "tidal";
    public const string BandcampKey = Prefix + "bandcamp";

    /// <summary>
    /// Returns the curated links for an artist. <paramref name="appleMusicUrl"/> and
    /// <paramref name="deezerUrl"/> are the exact links resolved from iTunes/Deezer, or null
    /// if they could not be resolved — in which case that service is simply absent, not faked.
    /// The search-based services are always present because a search URL is honest and cheap.
    /// </summary>
    public static Dictionary<string, string> Build(string artistName, string? appleMusicUrl, string? deezerUrl)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            throw new ArgumentException("Artist name is required to build streaming links.", nameof(artistName));
        }

        string encoded = Uri.EscapeDataString(artistName.Trim());

        Dictionary<string, string> links = new(StringComparer.Ordinal)
        {
            [SpotifyKey] = $"https://open.spotify.com/search/{encoded}",
            [YouTubeMusicKey] = $"https://music.youtube.com/search?q={encoded}",
            [YouTubeKey] = $"https://www.youtube.com/results?search_query={encoded}",
            [TidalKey] = $"https://tidal.com/search?q={encoded}",
            [BandcampKey] = $"https://bandcamp.com/search?q={encoded}",
        };

        if (!string.IsNullOrWhiteSpace(appleMusicUrl))
        {
            links[AppleMusicKey] = appleMusicUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(deezerUrl))
        {
            links[DeezerKey] = deezerUrl.Trim();
        }

        return links;
    }
}
