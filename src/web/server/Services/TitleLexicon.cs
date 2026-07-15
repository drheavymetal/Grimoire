using System.Globalization;
using System.Text;

namespace Grimoire.Server.Services;

/// <summary>
/// Song-title mining (C21): a <b>closed, honest vocabulary + a counter</b> over recording titles,
/// an approximation of lyrical theme that needs no Metal Archives (D17, SPEC §5.9). It is a
/// deliberately weak signal — a title is not a lyric — so the UI labels it an approximation and
/// never a curated fact.
///
/// <para>
/// The match is conservative: titles are normalised (lower-cased, diacritics stripped so
/// <c>frío</c> reads as <c>frio</c>) and split into whole words, and a theme is credited to a title
/// only when one of its <b>whole words</b> is in that theme's keyword set. "Deathcrush" does not
/// count toward <c>death</c>; "Death" does. A title counts at most once per theme (we measure how
/// many of a band's titles evoke a theme, not how many times a word repeats). The vocabulary is
/// bilingual (English + Spanish) because the catalogue is.
/// </para>
/// </summary>
public static class TitleLexicon
{
    /// <summary>A named lyrical theme and the number of a band's titles that evoke it.</summary>
    public readonly record struct ThemeCount(string Theme, int Count);

    // The closed vocabulary. Keys are stable theme ids (the UI translates them via i18next); values
    // are whole-word keywords, already normalised (lower-case, no diacritics). Kept modest and
    // defensible rather than exhaustive — this is an approximation, not a lexicon of metal.
    private static readonly IReadOnlyDictionary<string, string[]> Lexicon = new Dictionary<string, string[]>
    {
        ["death"] = ["death", "dead", "dying", "die", "died", "muerte", "muerto", "morir", "mortal", "grave", "tomb", "corpse", "cadaver"],
        ["blood"] = ["blood", "bloody", "bleed", "sangre", "gore"],
        ["war"] = ["war", "warfare", "battle", "soldier", "siege", "guerra", "batalla", "soldado"],
        ["winter"] = ["winter", "frost", "frozen", "cold", "snow", "ice", "invierno", "hielo", "nieve", "frio"],
        ["forest"] = ["forest", "forests", "woods", "woodland", "tree", "trees", "bosque", "arbol", "roots"],
        ["fire"] = ["fire", "flame", "flames", "burning", "burn", "blaze", "ashes", "fuego", "llama", "ceniza"],
        ["night"] = ["night", "nights", "midnight", "dusk", "twilight", "noche", "nocturnal"],
        ["darkness"] = ["dark", "darkness", "shadow", "shadows", "gloom", "oscuridad", "sombra", "tinieblas"],
        ["ritual"] = ["ritual", "rite", "rites", "ceremony", "sacrifice", "altar", "offering", "occult", "culto", "sacrificio"],
        ["cosmos"] = ["cosmos", "cosmic", "star", "stars", "void", "universe", "galaxy", "astral", "estrella", "vacio", "nebula"],
        ["religion"] = ["god", "gods", "heaven", "hell", "demon", "devil", "angel", "church", "dios", "infierno", "demonio", "iglesia"],
        ["sea"] = ["sea", "seas", "ocean", "waters", "river", "waves", "tide", "mar", "oceano", "rio", "marea"],
    };

    // Flatten to keyword -> theme for a single-pass lookup per word.
    private static readonly IReadOnlyDictionary<string, string> KeywordToTheme = BuildIndex();

    private static Dictionary<string, string> BuildIndex()
    {
        Dictionary<string, string> index = new(StringComparer.Ordinal);
        foreach ((string theme, string[] keywords) in Lexicon)
        {
            foreach (string keyword in keywords)
            {
                index[keyword] = theme;
            }
        }

        return index;
    }

    /// <summary>The theme ids the lexicon can report, for the UI to pre-register i18n labels.</summary>
    public static IReadOnlyCollection<string> Themes => (IReadOnlyCollection<string>)Lexicon.Keys;

    /// <summary>
    /// The keyword set for a mined theme id (C21), for the callers that filter recording titles by a
    /// theme's words — the Rite's mined lane and the browse door. Returns the theme's whole-word
    /// keywords (already normalised: lower-case, no diacritics), or an empty list when the id is not a
    /// known theme. An unknown theme matches nothing; it never widens the filter to everything.
    /// </summary>
    public static IReadOnlyList<string> KeywordsFor(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return [];
        }

        return Lexicon.TryGetValue(themeId.Trim().ToLowerInvariant(), out string[]? keywords)
            ? keywords
            : [];
    }

    /// <summary>
    /// Lower-cases a title and strips diacritics so <c>SKÁLD</c> reads as <c>skald</c> and
    /// <c>frío</c> as <c>frio</c> — the same tolerance the search uses. Non-letters are left in
    /// place; <see cref="CountThemes"/> splits on them.
    /// </summary>
    public static string Normalize(string title)
    {
        string lowered = title.ToLowerInvariant();
        string decomposed = lowered.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Counts, over a band's recording titles, how many titles evoke each theme (title-level: a
    /// title contributes at most once per theme). Returns only the themes that appear, ordered by
    /// count then theme id. A corpus with no thematic word yields an empty list — the honest empty
    /// state, never a fabricated theme.
    /// </summary>
    public static IReadOnlyList<ThemeCount> CountThemes(IEnumerable<string> titles)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        foreach (string title in titles)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            HashSet<string> themesInTitle = ThemesOf(title);
            foreach (string theme in themesInTitle)
            {
                counts[theme] = counts.GetValueOrDefault(theme) + 1;
            }
        }

        return counts
            .Select(kv => new ThemeCount(kv.Key, kv.Value))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Theme, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The distinct themes a single title evokes (its whole words, matched to the lexicon).</summary>
    public static HashSet<string> ThemesOf(string title)
    {
        HashSet<string> themes = new(StringComparer.Ordinal);
        string normalized = Normalize(title);
        StringBuilder word = new();

        void Flush()
        {
            if (word.Length > 0)
            {
                if (KeywordToTheme.TryGetValue(word.ToString(), out string? theme))
                {
                    themes.Add(theme);
                }

                word.Clear();
            }
        }

        foreach (char c in normalized)
        {
            if (char.IsLetter(c))
            {
                word.Append(c);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return themes;
    }
}
