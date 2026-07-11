using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Song-title mining (C21). These bite the boundaries that keep the approximation honest: whole-word
/// matching (so "Deathcrush" is not "death"), diacritic folding (so "Frío" reads as winter), and
/// title-level counting (a title counts once per theme, however many times a word repeats).
/// </summary>
public class TitleLexiconTests
{
    [Fact]
    public void ThemesOf_MatchesWholeWordsOnly()
    {
        // "Death" is the theme word; "Deathcrush" is a different word and must not count.
        Assert.Contains("death", TitleLexicon.ThemesOf("Cold Lake of Death"));
        Assert.DoesNotContain("death", TitleLexicon.ThemesOf("Deathcrush"));
    }

    [Fact]
    public void ThemesOf_FoldsDiacriticsAndCase()
    {
        // "Frío" → "frio" (winter), "INVIERNO" lower-cased — both Spanish winter words.
        Assert.Contains("winter", TitleLexicon.ThemesOf("Frío"));
        Assert.Contains("winter", TitleLexicon.ThemesOf("INVIERNO Eterno"));
    }

    [Fact]
    public void ThemesOf_CreditsSeveralThemesFromOneTitle()
    {
        System.Collections.Generic.HashSet<string> themes = TitleLexicon.ThemesOf("Blood and Death");
        Assert.Contains("blood", themes);
        Assert.Contains("death", themes);
    }

    [Fact]
    public void ThemesOf_NoThematicWordIsEmpty()
    {
        Assert.Empty(TitleLexicon.ThemesOf("Accumulation of Generalization"));
    }

    [Fact]
    public void CountThemes_IsTitleLevel_NotWordLevel()
    {
        // "Death Death" repeats the word but is one title that evokes death → count 1, not 2.
        var counts = TitleLexicon.CountThemes(["Death Death"]);
        ThemeCountOf(counts, "death", out int deathCount);
        Assert.Equal(1, deathCount);
    }

    [Fact]
    public void CountThemes_CountsHowManyTitlesEvokeEachTheme_OrderedByCount()
    {
        string[] titles =
        [
            "Winter",            // winter
            "Frozen Wastes",     // winter (frozen)
            "Snow",              // winter (snow)
            "Blood Eagle",       // blood
            "A Quiet Afternoon", // nothing
        ];

        var counts = TitleLexicon.CountThemes(titles);

        // winter appears in three titles, blood in one, and winter must rank first.
        Assert.Equal("winter", counts[0].Theme);
        Assert.Equal(3, counts[0].Count);
        ThemeCountOf(counts, "blood", out int bloodCount);
        Assert.Equal(1, bloodCount);
    }

    [Fact]
    public void CountThemes_EmptyCorpusIsEmpty()
    {
        Assert.Empty(TitleLexicon.CountThemes([]));
    }

    private static void ThemeCountOf(
        System.Collections.Generic.IReadOnlyList<TitleLexicon.ThemeCount> counts,
        string theme,
        out int count)
    {
        foreach (TitleLexicon.ThemeCount tc in counts)
        {
            if (tc.Theme == theme)
            {
                count = tc.Count;
                return;
            }
        }

        count = 0;
    }
}
