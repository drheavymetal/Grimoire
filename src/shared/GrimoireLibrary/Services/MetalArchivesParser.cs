using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grimoire.Library.Services;

/// <summary>
/// One candidate row from Metal Archives' <c>ajax-band-search</c> endpoint: the band id and name
/// parsed out of the result link, plus the genre and country columns. The search is fuzzy
/// ("Nirvana" returns "Nirvana 2002", "Nirvana420", "Voids of Nirvana"), so a candidate is only a
/// real match after <see cref="MetalArchivesParser.Match"/> checks the normalised name and country.
/// </summary>
public sealed record MetalArchivesCandidate(int Id, string Name, string Genre, string Country);

/// <summary>
/// The subset of a Metal Archives band page Grimoire keeps: the id (for the Metallum link required
/// by Invariant 3), the canonical name, country, formation year, status, genre and — the one field
/// that exists nowhere else (D48/Q4) — the lyrical themes. Line-up, review scores and images are
/// deliberately out of scope for this pass (D44/D49: a later, more careful merge).
/// </summary>
public sealed record MetalArchivesBand(
    int Id,
    string Name,
    string? Country,
    int? YearFormed,
    string? Status,
    string? Genre,
    string[] Themes);

/// <summary>
/// Pure parsing and conservative matching for Metal Archives, kept out of the HTTP layer so it can
/// be tested without a network (mirrors <see cref="LastFmListeners"/>). Everything here honours the
/// scrape terms agreed with MA (D42/D48): no guessing on identity — an ambiguous search resolves to
/// <c>null</c>, never a wrong band. Matching is by <b>name + country + year</b> because MA holds no
/// MusicBrainz ids (D48/R3), reusing the same diacritic-insensitive <see cref="NameMatch"/> as the
/// preview and listeners passes (D25).
/// </summary>
public static class MetalArchivesParser
{
    // Result link, e.g. <a href="https://www.metal-archives.com/bands/Darkthrone/146">Darkthrone</a>.
    private static readonly Regex BandLink = new(
        @"/bands/[^""/]+/(\d+)""[^>]*>(.*?)</a>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // A <dt>Label:</dt> <dd ...>value</dd> pair inside the band_stats block.
    private static readonly Regex StatPair = new(
        @"<dt>(.*?)</dt>\s*<dd[^>]*>(.*?)</dd>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Year = new(@"\d{4}", RegexOptions.Compiled);

    /// <summary>
    /// Parses the <c>aaData</c> rows of an <c>ajax-band-search</c> response into candidates. Each row
    /// is <c>[ linkHtml, genre, country ]</c>; the id and name come out of the link. Returns an empty
    /// list on error, malformed JSON, or no results — never throws for the job's sake.
    /// </summary>
    public static IReadOnlyList<MetalArchivesCandidate> ParseSearch(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        List<MetalArchivesCandidate> candidates = [];

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("aaData", out JsonElement rows)
                || rows.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 3)
                {
                    continue;
                }

                string linkHtml = row[0].GetString() ?? string.Empty;
                Match m = BandLink.Match(linkHtml);

                if (!m.Success || !int.TryParse(m.Groups[1].Value, out int id))
                {
                    continue;
                }

                string name = StripTags(m.Groups[2].Value);
                string genre = StripTags(row[1].GetString() ?? string.Empty);
                string country = StripTags(row[2].GetString() ?? string.Empty);

                if (name.Length > 0)
                {
                    candidates.Add(new MetalArchivesCandidate(id, name, genre, country));
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return candidates;
    }

    /// <summary>
    /// Picks the one candidate that matches our band, or <c>null</c> when none or more than one does
    /// — the conservative rule of D48: better no match than the wrong band. A candidate matches when
    /// its name equals ours after normalisation (<see cref="NameMatch"/>) and, when both countries
    /// are known, the country matches too. If exactly one survives, it is returned; two survivors are
    /// an ambiguity and yield <c>null</c> (year disambiguation then happens against the band page).
    /// </summary>
    public static MetalArchivesCandidate? Match(
        IReadOnlyList<MetalArchivesCandidate> candidates, string name, string? country)
    {
        List<MetalArchivesCandidate> hits = [];

        foreach (MetalArchivesCandidate c in candidates)
        {
            if (!NameMatch.Matches(c.Name, name))
            {
                continue;
            }

            // Country is a strong discriminator when we have both; a blank on either side is not
            // treated as a mismatch (some catalogue rows carry no country).
            if (!string.IsNullOrWhiteSpace(country)
                && !string.IsNullOrWhiteSpace(c.Country)
                && !CountryMatches(c.Country, country))
            {
                continue;
            }

            hits.Add(c);
        }

        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>
    /// Parses a band page into a <see cref="MetalArchivesBand"/>. Reads the <c>band_stats</c> dt/dd
    /// block for country, status, formation year, genre and lyrical themes. Returns <c>null</c> only
    /// when the page has no recognisable stats block; missing individual fields become nulls, never
    /// invented (Invariant 5).
    /// </summary>
    public static MetalArchivesBand? ParseBand(string? html, int id, string name)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        // Scope to the stats block so stray dt/dd elsewhere on the page cannot leak in.
        string scope = html;
        int start = html.IndexOf("band_stats", StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = html.IndexOf("</div>", start, StringComparison.Ordinal);
            scope = end > start ? html[start..end] : html[start..];
        }

        string? country = null, status = null, genre = null;
        int? year = null;
        string[] themes = [];
        bool sawAny = false;

        foreach (Match pair in StatPair.Matches(scope))
        {
            string key = StripTags(pair.Groups[1].Value).TrimEnd(':').Trim();
            string value = StripTags(pair.Groups[2].Value);
            sawAny = true;

            switch (key.ToLowerInvariant())
            {
                case "country of origin":
                    country = NullIfBlank(value);
                    break;
                case "status":
                    status = NullIfBlank(value);
                    break;
                case "formed in":
                    Match y = Year.Match(value);
                    year = y.Success ? int.Parse(y.Value) : null;
                    break;
                case "genre":
                    genre = NullIfBlank(value);
                    break;
                case "themes":
                case "lyrical themes":
                    themes = ParseThemes(value);
                    break;
            }
        }

        if (!sawAny)
        {
            return null;
        }

        return new MetalArchivesBand(id, name, country, year, status, genre, themes);
    }

    /// <summary>
    /// Splits a themes string ("Anti-religion, Satan, Occultism, Death") into a trimmed, de-duplicated
    /// list. Commas and semicolons both separate; parenthetical era markers ("(early)") are kept as
    /// part of the theme since MA uses them meaningfully. Empty or "N/A" yields an empty array.
    /// </summary>
    public static string[] ParseThemes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0 && !t.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool CountryMatches(string a, string b)
    {
        return NameMatch.Normalize(a) == NameMatch.Normalize(b);
    }

    private static string StripTags(string html)
    {
        return System.Net.WebUtility.HtmlDecode(Tag.Replace(html, string.Empty)).Trim();
    }

    private static string? NullIfBlank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
