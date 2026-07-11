using System.Globalization;
using System.Text.Json.Serialization;

namespace Grimoire.Library.Services;

/// <summary>
/// Pure parsing and verification for Last.fm <c>artist.getInfo</c>, kept out of the HTTP layer
/// so it can be tested without a network.
/// <para>
/// Two identity strategies, both honouring DECISIONS D25 ("better a missing count than the wrong
/// band's"). The preferred one, <see cref="ParseListeners"/>, is used when the request was made
/// <b>by MusicBrainz id</b>: Last.fm then returns exactly the entity we asked for, so no name
/// guessing is needed and same-name collisions (the "Toto"/"Death" problem of D22) cannot happen.
/// The fallback, <see cref="Resolve"/>, is for the rare artist with no mbid: it matches by name
/// (<see cref="NameMatch"/>) and rejects when Last.fm hands back a contradicting id. Either way an
/// ambiguous or mismatched result resolves to <c>null</c>, and a null count derives a null
/// <see cref="RankCalculator">rank</see>. Nothing is invented.
/// </para>
/// </summary>
public static class LastFmListeners
{
    /// <summary>
    /// Extracts the listener count from a getInfo response fetched <b>by MusicBrainz id</b>, where
    /// the returned entity is the one we asked for by construction. Returns <c>null</c> when the
    /// response is missing, is an error (e.g. code 6, Last.fm does not index that id), or has no
    /// parseable count. A null count then derives a null rank.
    /// </summary>
    public static int? ParseListeners(LastFmArtistInfoResponse? response)
    {
        if (response is null || response.Error is not null)
        {
            return null;
        }

        string? raw = response.Artist?.Stats?.Listeners;

        if (string.IsNullOrWhiteSpace(raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int listeners)
            || listeners < 0)
        {
            return null;
        }

        return listeners;
    }

    /// <summary>
    /// Name-path fallback for an artist with no MusicBrainz id: extracts the listener count only
    /// when the returned band's name matches and no contradicting id is present. Returns
    /// <c>null</c> otherwise. Prefer <see cref="ParseListeners"/> whenever an mbid is available.
    /// </summary>
    /// <param name="response">The deserialised getInfo body, or null if none arrived.</param>
    /// <param name="expectedName">The catalogue artist name we asked about.</param>
    /// <param name="expectedMbid">The catalogue MusicBrainz id, or <see cref="Guid.Empty"/> if unknown.</param>
    public static int? Resolve(LastFmArtistInfoResponse? response, string expectedName, Guid expectedMbid)
    {
        if (response is null || response.Error is not null)
        {
            return null;
        }

        LastFmArtist? artist = response.Artist;

        if (artist is null || string.IsNullOrWhiteSpace(artist.Name))
        {
            return null;
        }

        if (!NameMatch.Matches(artist.Name, expectedName))
        {
            // Last.fm returned a different band under the same query — reject (D25).
            return null;
        }

        if (expectedMbid != Guid.Empty
            && !string.IsNullOrWhiteSpace(artist.Mbid)
            && Guid.TryParse(artist.Mbid, out Guid returnedMbid)
            && returnedMbid != expectedMbid)
        {
            // Name collides but the MusicBrainz id disagrees: it is the wrong entity.
            return null;
        }

        return ParseListeners(response);
    }
}

/// <summary>Envelope of Last.fm <c>artist.getInfo</c>. On failure Last.fm returns an
/// <c>error</c> code (e.g. 6 = artist not found) instead of an <c>artist</c> object.</summary>
public sealed class LastFmArtistInfoResponse
{
    [JsonPropertyName("artist")]
    public LastFmArtist? Artist { get; set; }

    [JsonPropertyName("error")]
    public int? Error { get; set; }
}

public sealed class LastFmArtist
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mbid")]
    public string? Mbid { get; set; }

    [JsonPropertyName("stats")]
    public LastFmStats? Stats { get; set; }
}

public sealed class LastFmStats
{
    /// <summary>Last.fm serialises the listener count as a string; parsing lives in <see cref="LastFmListeners.Resolve"/>.</summary>
    [JsonPropertyName("listeners")]
    public string? Listeners { get; set; }
}
