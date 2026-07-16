using System.Linq.Expressions;
using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// One biography ready to show: the edition it came from, the text, and the article to credit.
/// <see cref="Url"/> is nullable only because a biography stored before the URL was captured would
/// otherwise vanish from the page entirely; the pass never writes text without it (a summary that
/// yields no URL is discarded, not stored), so in practice it is always present and CC BY-SA
/// attribution always has somewhere to point.
/// </summary>
public sealed record ArtistBiographyView(string Language, string Abstract, string? Url);

/// <summary>
/// Reads an artist's biographies as the single list the rest of the system wants, out of the two
/// places they are actually stored: English on <see cref="Artist.Abstract"/>, every other language
/// in <see cref="ArtistBiography"/> rows.
/// <para>
/// That split is a deliberate concession, not a design (see <see cref="ArtistBiography"/>): English
/// is pinned to the artists table because <c>EmbeddingTextBuilder</c> reads it there, and moving it
/// would re-fingerprint and re-embed the whole catalogue (D62). This function is the seam that makes
/// the concession invisible — callers get languages, not storage trivia, and no caller should reach
/// past it for <c>Abstract</c> directly.
/// </para>
/// </summary>
public static class ArtistBiographies
{
    /// <summary>The edition that lives on the artist row rather than in the child table.</summary>
    public const string English = "en";

    /// <summary>Whether a language code names the English edition, however it was cased in configuration.</summary>
    public static bool IsEnglish(string language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The artists owing a lookup in at least one of <paramref name="configured"/>, as a predicate
    /// PostgreSQL can run. The in-database twin of <see cref="PendingLanguages"/>, and they must agree:
    /// this one decides who is fetched, that one decides what is asked about them. If a row passed
    /// here and then had nothing pending, the pass would fetch it, ask nothing, save nothing, and meet
    /// it again on the next run — for ever. That is the shape of every bug in D61, so the two rules
    /// live side by side rather than in a job and a query a thousand lines apart.
    /// <para>
    /// It exists as an expression rather than a LINQ filter written in place so it can be pushed into
    /// SQL: these rows carry a 768-dimension embedding, and pulling the catalogue into memory to pick
    /// 500 of them is exactly the mistake D61 found in <c>ListenersJob</c>. The non-English half is a
    /// correlated count over the child table's primary key — the anti-join a jsonb column could not
    /// have offered.
    /// </para>
    /// </summary>
    public static Expression<Func<Artist, bool>> PendingPredicate(IReadOnlyList<string> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        bool wantsEnglish = configured.Any(IsEnglish);

        // Lower-cased to match what the pass stores, because `= ANY(...)` in SQL is case-sensitive
        // while PendingLanguages compares case-insensitively. Left alone, a "ES" in configuration
        // would make this predicate report an artist pending that PendingLanguages considers done:
        // fetched every run, asked nothing, never resolved. The two must not be able to disagree.
        List<string> others = configured
            .Where(l => !IsEnglish(l))
            .Select(l => l.ToLowerInvariant())
            .ToList();

        int wanted = others.Count;

        return artist => artist.Mbid != Guid.Empty
            && ((wantsEnglish && artist.Abstract == null && artist.AbstractCheckedAt == null)
                || artist.Biographies.Count(b => others.Contains(b.Language)) < wanted);
    }

    /// <summary>
    /// Which of <paramref name="configured"/> still need looking up for this artist — the per-language
    /// resume marker, read. Order follows <paramref name="configured"/>.
    /// <para>
    /// The two storage halves answer "have we checked?" differently, and both answers are load-bearing.
    /// English is checked when it has text <em>or</em> a stamp: <c>abstract_checked_at</c> is set on
    /// 206 882 rows, which is exactly why no other language may consult it — a Spanish pass that did
    /// would find the catalogue "done" and visit nobody. Every other language is checked when its row
    /// exists at all, matched or not, because the pass writes a row only on a definitive answer: a
    /// WDQS timeout leaves no row, and the artist comes back round (D61).
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> PendingLanguages(Artist artist, IReadOnlyList<string> configured)
    {
        ArgumentNullException.ThrowIfNull(artist);
        ArgumentNullException.ThrowIfNull(configured);

        List<string> pending = [];

        foreach (string language in configured)
        {
            bool alreadyChecked = IsEnglish(language)
                ? artist.Abstract is not null || artist.AbstractCheckedAt is not null
                : artist.Biographies.Any(b => string.Equals(b.Language, language, StringComparison.OrdinalIgnoreCase));

            if (!alreadyChecked)
            {
                pending.Add(language);
            }
        }

        return pending;
    }

    /// <summary>
    /// Every biography this artist actually has text for, English first and the rest by language
    /// code — a fixed order so "whatever is available" is a reproducible choice rather than whatever
    /// the database happened to return. Checked-but-absent rows (the honest gaps, text null) are
    /// left out: this returns what can be shown, not what was searched. An <c>en</c> row in the child
    /// table is ignored, so a stray write there can never shadow the canonical English text or, worse,
    /// show the reader two different English biographies.
    /// </summary>
    public static IReadOnlyList<ArtistBiographyView> Merge(Artist artist)
    {
        ArgumentNullException.ThrowIfNull(artist);

        List<ArtistBiographyView> merged = [];

        if (!string.IsNullOrWhiteSpace(artist.Abstract))
        {
            merged.Add(new ArtistBiographyView(English, artist.Abstract.Trim(), artist.AbstractUrl));
        }

        IEnumerable<ArtistBiography> others = artist.Biographies
            .Where(b => !string.IsNullOrWhiteSpace(b.Abstract))
            .Where(b => !string.Equals(b.Language, English, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.Language, StringComparer.Ordinal);

        foreach (ArtistBiography biography in others)
        {
            merged.Add(new ArtistBiographyView(
                biography.Language.ToLowerInvariant(),
                biography.Abstract!.Trim(),
                biography.AbstractUrl));
        }

        return merged;
    }
}
