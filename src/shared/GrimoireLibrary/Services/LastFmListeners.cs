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

    /// <summary>
    /// Name-only verification for the mbid-then-name fallback (DECISIONS D41). Accepts the listener
    /// count when the returned band's name matches (<see cref="NameMatch"/>), <b>even if Last.fm's
    /// own mbid differs from ours</b> — Last.fm frequently indexes a band under a different
    /// MusicBrainz id than the one in our catalogue, so the id lookup misses even famous bands; the
    /// name lookup recovers them. The name match still keeps a same-name band from lending its
    /// listeners; a differing mbid is deliberately not treated as wrong-band here (the coverage/
    /// precision trade-off Pedro ratified in D41). Returns <c>null</c> on error, missing artist, or
    /// name mismatch.
    /// </summary>
    public static int? ResolveByName(LastFmArtistInfoResponse? response, string expectedName)
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
            return null;
        }

        return ParseListeners(response);
    }

    /// <summary>
    /// Non-genre folksonomy tags that Last.fm users attach in bulk. They carry no descriptive
    /// signal for the embedding text (D26) and would only pull unrelated bands together on the
    /// map, so they are dropped. Compared case-insensitively against the lower-cased tag.
    /// </summary>
    private static readonly HashSet<string> JunkTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "seen live", "favorites", "favourites", "favorite", "favourite",
        "albums i own", "vinyl", "spotify", "under 2000 listeners",
        "beautiful", "awesome", "amazing", "cool", "good", "love", "loved",
        "male vocalists", "female vocalists", "female fronted metal",
        "check out", "to check out", "want to see live",
    };

    /// <summary>
    /// Extracts up to five genre tags from a getInfo response, most-voted first (Last.fm returns
    /// its top tags in descending order). Junk folksonomy (<see cref="JunkTags"/>) and blanks are
    /// dropped, duplicates collapsed case-insensitively. Returns an empty array — never null —
    /// when the response is missing, an error, or carries no usable tags. Parses tags from
    /// <b>whichever</b> getInfo body resolved the artist, so listeners and tags come from the one
    /// same call (MEMORY §6b: <c>artist.getInfo</c> returns both at once).
    /// </summary>
    public static string[] ParseTags(LastFmArtistInfoResponse? response)
    {
        if (response is null || response.Error is not null)
        {
            return [];
        }

        List<LastFmTag>? tags = response.Artist?.Tags?.Tag;

        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        return tags
            .Select(t => t.Name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Where(name => !JunkTags.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
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

    /// <summary>Top genre tags, in descending vote order. Parsed by <see cref="LastFmListeners.ParseTags"/>.</summary>
    [JsonPropertyName("tags")]
    public LastFmTagList? Tags { get; set; }
}

public sealed class LastFmStats
{
    /// <summary>Last.fm serialises the listener count as a string; parsing lives in <see cref="LastFmListeners.Resolve"/>.</summary>
    [JsonPropertyName("listeners")]
    public string? Listeners { get; set; }
}

/// <summary>The <c>tags</c> object of a getInfo response: <c>{ "tag": [ { "name": ... } ] }</c>.</summary>
public sealed class LastFmTagList
{
    [JsonPropertyName("tag")]
    public List<LastFmTag>? Tag { get; set; }
}

public sealed class LastFmTag
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
