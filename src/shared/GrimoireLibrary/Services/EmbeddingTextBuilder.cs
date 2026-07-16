using System.Security.Cryptography;
using System.Text;
using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// Builds the rich text embedded per artist for the discovery engine — variant C of
/// spike v3b (DECISIONS D26): name, tags, country, members, label and abstract when
/// present. Variant D (tags only) was rejected because it collapses the ~17 % of bands
/// with no tags onto a single point; the name and the surrounding context keep every
/// artist's text distinct before the corpus mean is subtracted.
/// </summary>
public static class EmbeddingTextBuilder
{
    /// <summary>
    /// Assembles the embedding text. <paramref name="memberNames"/> and
    /// <paramref name="labelNames"/> come from the graph the ETL already populated; both
    /// may be empty, in which case their clause is simply omitted (no invented filler).
    /// Returns null when the artist carries no signal at all (no tags, abstract, members,
    /// label, country or city) so the caller can leave its embedding null instead of
    /// embedding a bare name.
    /// </summary>
    public static string? Build(
        Artist artist,
        IReadOnlyList<string>? memberNames = null,
        IReadOnlyList<string>? labelNames = null)
    {
        ArgumentNullException.ThrowIfNull(artist);

        bool hasSignal =
            (artist.Tags is { Length: > 0 })
            || !string.IsNullOrWhiteSpace(artist.Abstract)
            || !string.IsNullOrWhiteSpace(artist.Country)
            || !string.IsNullOrWhiteSpace(artist.City)
            || (memberNames is { Count: > 0 })
            || (labelNames is { Count: > 0 });

        if (!hasSignal)
        {
            return null;
        }

        StringBuilder sb = new();

        sb.Append(artist.Name.Trim());
        sb.Append('.');

        string? place = FormatPlace(artist.City, artist.Country);

        if (place is not null)
        {
            sb.Append(' ').Append(artist.Kind == ArtistKind.Person ? "From " : "Group from ").Append(place).Append('.');
        }

        if (artist.Tags is { Length: > 0 })
        {
            sb.Append(" Genres: ").Append(string.Join(", ", artist.Tags)).Append('.');
        }

        if (memberNames is { Count: > 0 })
        {
            sb.Append(" Members: ").Append(string.Join(", ", memberNames)).Append('.');
        }

        if (labelNames is { Count: > 0 })
        {
            sb.Append(" Labels: ").Append(string.Join(", ", labelNames)).Append('.');
        }

        if (!string.IsNullOrWhiteSpace(artist.Abstract))
        {
            sb.Append(' ').Append(artist.Abstract.Trim());
        }

        return sb.ToString();
    }

    /// <summary>
    /// A short, stable fingerprint of an embedding text — the value stored in
    /// <see cref="Artist.EmbeddingFingerprint"/> beside the vector it produced. Comparing it against
    /// the text's fingerprint today tells the embedding pass whether the stored vector still
    /// describes this artist, so enrichment (new tags, a new biography, a new member) re-embeds
    /// exactly the artists whose text moved and leaves the rest alone.
    /// <para>
    /// SHA-256 truncated to 128 bits, hex. Truncation is safe here: this is a change detector, not
    /// a security boundary, and at catalogue scale (~200k rows) a 128-bit collision — which would
    /// merely leave one artist on a stale vector — is not a thing that happens.
    /// </para>
    /// </summary>
    public static string Fingerprint(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private static string? FormatPlace(string? city, string? country)
    {
        bool hasCity = !string.IsNullOrWhiteSpace(city);
        bool hasCountry = !string.IsNullOrWhiteSpace(country);

        if (hasCity && hasCountry)
        {
            return $"{city!.Trim()}, {country!.Trim()}";
        }

        if (hasCity)
        {
            return city!.Trim();
        }

        if (hasCountry)
        {
            return country!.Trim();
        }

        return null;
    }
}
