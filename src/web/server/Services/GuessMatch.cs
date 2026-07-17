using Grimoire.Library.Services;

namespace Grimoire.Server.Services;

/// <summary>
/// Judges a typed band name in the Hard difficulty of "guess the band" (D67). Pure, so the one rule
/// that decides whether a player was right can be held to its promises without a database.
///
/// <para>
/// <b>The brief was two-sided, and both sides are real.</b> Failing somebody for a missing accent is
/// infuriating and would make Hard a typing test rather than a music one. Accepting a name that is
/// actually a DIFFERENT band is worse than infuriating: it quietly stops measuring anything, which is
/// the failure mode this whole feature was scoped to avoid. So generosity is spent in a specific
/// order, and it runs out the moment another band is in reach.
/// </para>
/// <para>
/// <b>Three gates, in this order.</b>
/// (1) Normalise and compare (<see cref="NameMatch"/> — the same case/diacritic/whitespace folding the
/// preview matcher has always used, already covered by its own tests: <c>SKÁLD</c> = <c>skald</c>,
/// <c>Motörhead</c> = <c>motorhead</c>, <c>Old Man's Child</c> tolerant of its spacing). This is where
/// the accent problem dies, and it dies for free — before any distance is measured, so no threshold
/// can ever reintroduce it.
/// (2) If the normalised guess IS the exact name of some other band, it is wrong, full stop, however
/// close it sits to the answer. The player named a band. It was not this one.
/// (3) Only then, typos: within an edit budget scaled to the name's length, AND strictly closer to the
/// answer than to any other band the player could have meant.
/// </para>
/// <para>
/// The comparison is deliberately NOT pg_trgm's <c>similarity()</c>, though the index is what supplies
/// gate 2's candidates. Trigram overlap is the right tool for ranking a search box against 206k rows;
/// it is the wrong tool for adjudicating one string against one known answer, where "how many
/// keystrokes off were you" is the question actually being asked. <c>Mayhem</c> and <c>Mayhemic</c>
/// score 0.75 on trigrams — comfortably inside any threshold loose enough to accept <c>darkthron</c> —
/// and they are two different bands.
/// </para>
/// </summary>
public static class GuessMatch
{
    /// <summary>
    /// How many single-character mistakes are forgiven, by the length of the correct name. A short
    /// name gets nothing: at four characters one edit reaches half the language, and <c>Tool</c> is one
    /// step from <c>Toon</c>, <c>Tool</c>, <c>Cool</c> and <c>Toad</c>. Long names get two, because a
    /// long name is more to mistype and its neighbourhood is emptier — a fifteen-character string with
    /// two edits is still a name nobody else has.
    /// </summary>
    public static int EditBudget(int nameLength)
    {
        if (nameLength <= 4)
        {
            return 0;
        }

        return nameLength <= 11 ? 1 : 2;
    }

    /// <summary>
    /// Whether a typed guess names the answer. <paramref name="otherNames"/> is every other band the
    /// player could plausibly have meant — their other summons (the decoy universe of this very game)
    /// plus whatever the catalogue's trigram index says sits near the guess. It is what turns "close
    /// enough" into "close enough, and not ambiguous": generosity is only safe while nothing else is
    /// standing there.
    /// </summary>
    /// <param name="guess">What the player typed.</param>
    /// <param name="answerName">The band's real name.</param>
    /// <param name="otherNames">Other real band names in reach. Empty is fine — then only the budget applies.</param>
    public static bool IsCorrect(string? guess, string answerName, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(answerName);
        ArgumentNullException.ThrowIfNull(otherNames);

        string typed = NameMatch.Normalize(guess);
        string answer = NameMatch.Normalize(answerName);

        if (typed.Length == 0 || answer.Length == 0)
        {
            return false;
        }

        // Gate 1 — the accent case, and the overwhelmingly common one. Free, and before any threshold.
        if (typed == answer)
        {
            return true;
        }

        List<string> others = otherNames
            .Select(NameMatch.Normalize)
            .Where(n => n.Length > 0 && n != answer)
            .ToList();

        // Gate 2 — they named a band, and it was a different one. No budget can rescue this: an exact
        // hit on another name is a statement, not a slip.
        if (others.Any(other => other == typed))
        {
            return false;
        }

        int budget = EditBudget(answer.Length);

        if (budget == 0)
        {
            return false;
        }

        int distance = Distance(typed, answer, budget);

        if (distance > budget)
        {
            return false;
        }

        // Gate 3 — the guess must be closer to the answer than to anything else in reach. A tie is a
        // failure, not a coin flip: if two bands are equally good readings of what they typed, the
        // player did not name one of them.
        return others.All(other => Distance(typed, other, distance) > distance);
    }

    /// <summary>
    /// Edit distance between two normalised names, abandoned once it is provably greater than
    /// <paramref name="limit"/>.
    ///
    /// <para>
    /// <b>Optimal string alignment, not plain Levenshtein</b> — the difference is one line and it
    /// matters. Swapping two adjacent letters is one of the commonest ways a human mistypes a word, and
    /// plain Levenshtein charges TWO edits for it: <c>darkthorne</c> would fail against
    /// <c>Darkthrone</c>, on a ten-letter name, for a slip nobody would call two mistakes. Counting a
    /// transposition as the single action it is keeps the budget meaning "one slip" instead of "one
    /// slip, unless it was that kind". It costs no generosity where it counts: <c>mayhem</c> and
    /// <c>mayhemic</c> are two insertions under either rule, and stay two.
    /// </para>
    /// <para>
    /// The ceiling is not an optimisation for its own sake: every caller only ever cares whether the
    /// distance is small, and a band name typed into a text field is unbounded input. It may only ever
    /// refuse to count PAST the limit — never make a small distance look large — or it would throw out
    /// real answers to save arithmetic.
    /// </para>
    /// </summary>
    public static int Distance(string a, string b, int limit)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a == b)
        {
            return 0;
        }

        // A length gap alone already exceeds the limit: no alignment can close it.
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        // Three rolling rows: a transposition reaches back two.
        int[] twoAgo = new int[b.Length + 1];
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int best = current[0];

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;

                int value = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                // The two letters are each other's, the wrong way round: one action, not two.
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, twoAgo[j - 2] + 1);
                }

                current[j] = value;
                best = Math.Min(best, value);
            }

            // Every alignment through this row already costs more than the caller can accept.
            if (best > limit)
            {
                return limit + 1;
            }

            (twoAgo, previous, current) = (previous, current, twoAgo);
        }

        return previous[b.Length];
    }
}
