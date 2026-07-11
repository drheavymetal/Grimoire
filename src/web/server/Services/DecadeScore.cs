namespace Grimoire.Server.Services;

/// <summary>The outcome of one guessed dimension in the decade game (feature C27).</summary>
public enum GuessOutcome
{
    /// <summary>Exactly right (the decade, the country, or one of the band's subgenres).</summary>
    Hit,

    /// <summary>Close but not exact — only the decade earns this, when the guess is one decade off.</summary>
    Close,

    /// <summary>Wrong.</summary>
    Miss,
}

/// <summary>What the player bet in one round of "guess the decade" (feature C27).</summary>
public sealed record DecadeGuess(int Decade, string? Country, string? Subgenre);

/// <summary>The truth a guess is scored against: the band's real formed year, country and tags.</summary>
public sealed record DecadeTruth(int? FormedYear, string? Country, IReadOnlyList<string> Tags);

/// <summary>One scored dimension: what outcome it earned and how many points.</summary>
public sealed record DimensionScore(GuessOutcome Outcome, int Points);

/// <summary>The full round score: per-dimension outcomes and the running totals.</summary>
public sealed record RoundScore(
    DimensionScore Decade,
    DimensionScore Country,
    DimensionScore Subgenre)
{
    /// <summary>Points earned this round.</summary>
    public int Total => Decade.Points + Country.Points + Subgenre.Points;

    /// <summary>The most a round can earn, so the front can show "N of M".</summary>
    public static int MaxPoints => DecadeScore.DecadeHitPoints + DecadeScore.CountryHitPoints + DecadeScore.SubgenreHitPoints;
}

/// <summary>
/// Scores one round of "guess the decade" (feature C27): 45 seconds blind, then the player bets a
/// decade, a country and a subgenre, and is scored against the band's real data. It trains the ear,
/// which is literally the app's mission (SPEC §5.7). Pure and deterministic — no database, unit
/// tested on the boundaries (an exact decade, one decade off, two decades off; case and whitespace
/// in the country; a subgenre that is a token of a tag versus one that is not).
///
/// <para>
/// The decade game only ever serves <b>scorable</b> bands — those with a formed year, a country and
/// at least one tag (the engine filters the pool) — so every dimension can be judged against a real
/// value, never an invented one (REVIEW.md: no fabrication). The null-tolerant branches here are a
/// belt-and-braces guard, scoring a missing truth as a Miss rather than throwing.
/// </para>
/// </summary>
public static class DecadeScore
{
    /// <summary>Points for naming the exact decade.</summary>
    public const int DecadeHitPoints = 2;

    /// <summary>Points for landing one decade either side.</summary>
    public const int DecadeClosePoints = 1;

    /// <summary>Points for the exact country.</summary>
    public const int CountryHitPoints = 1;

    /// <summary>Points for naming a subgenre the band actually carries.</summary>
    public const int SubgenreHitPoints = 1;

    /// <summary>Scores a guess against the band's truth.</summary>
    public static RoundScore Score(DecadeGuess guess, DecadeTruth truth)
    {
        ArgumentNullException.ThrowIfNull(guess);
        ArgumentNullException.ThrowIfNull(truth);

        return new RoundScore(
            ScoreDecade(guess.Decade, truth.FormedYear),
            ScoreCountry(guess.Country, truth.Country),
            ScoreSubgenre(guess.Subgenre, truth.Tags));
    }

    /// <summary>Normalises any year to the start of its decade: 1987 → 1980, 1990 → 1990.</summary>
    public static int DecadeOf(int year)
    {
        return (int)(Math.Floor(year / 10.0) * 10);
    }

    private static DimensionScore ScoreDecade(int guessYear, int? formedYear)
    {
        if (formedYear is not int year)
        {
            return new DimensionScore(GuessOutcome.Miss, 0);
        }

        int guessed = DecadeOf(guessYear);
        int actual = DecadeOf(year);
        int gap = Math.Abs(actual - guessed);

        if (gap == 0)
        {
            return new DimensionScore(GuessOutcome.Hit, DecadeHitPoints);
        }

        if (gap == 10)
        {
            return new DimensionScore(GuessOutcome.Close, DecadeClosePoints);
        }

        return new DimensionScore(GuessOutcome.Miss, 0);
    }

    private static DimensionScore ScoreCountry(string? guess, string? actual)
    {
        if (string.IsNullOrWhiteSpace(guess) || string.IsNullOrWhiteSpace(actual))
        {
            return new DimensionScore(GuessOutcome.Miss, 0);
        }

        bool hit = string.Equals(guess.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
        return new DimensionScore(hit ? GuessOutcome.Hit : GuessOutcome.Miss, hit ? CountryHitPoints : 0);
    }

    private static DimensionScore ScoreSubgenre(string? guess, IReadOnlyList<string> tags)
    {
        if (string.IsNullOrWhiteSpace(guess) || tags is null || tags.Count == 0)
        {
            return new DimensionScore(GuessOutcome.Miss, 0);
        }

        string needle = guess.Trim();

        // A hit is a genuine token overlap either way: the guess is a tag ("black metal"), or the
        // guess names a word inside a tag ("black" for "black metal"), or a tag is a word inside the
        // guess ("atmospheric black metal" for the tag "black metal"). Case-insensitive throughout.
        bool hit = tags.Any(tag =>
            !string.IsNullOrWhiteSpace(tag)
            && (tag.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || needle.Contains(tag.Trim(), StringComparison.OrdinalIgnoreCase)));

        return new DimensionScore(hit ? GuessOutcome.Hit : GuessOutcome.Miss, hit ? SubgenreHitPoints : 0);
    }
}
