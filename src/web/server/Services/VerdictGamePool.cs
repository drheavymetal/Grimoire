using Grimoire.Library.Models;

namespace Grimoire.Server.Services;

/// <summary>
/// Why a verdict game cannot be dealt. Every value is a fact about real data, and each maps to its
/// own honest empty state in the front — "your friend has not banished anything yet" is a different
/// sentence from "your friend has barely played", and telling them apart is the whole point (R2: the
/// app degrades, it does not break, and it never fakes a round to fill a screen).
/// </summary>
public enum VerdictGameBlocker
{
    /// <summary>Playable.</summary>
    None,

    /// <summary>Fewer resolved rites than a game needs. Served and Again do not count — they are not verdicts.</summary>
    TooFewVerdicts,

    /// <summary>Every resolved rite is a summon: with one possible answer the game tests nothing.</summary>
    NoBanishments,

    /// <summary>Every resolved rite is a banishment: same degeneracy, mirrored.</summary>
    NoSummons,

    /// <summary>The verdicts are there but too few of the bands can be made to sound (D25/D40).</summary>
    NotEnoughAudible,
}

/// <summary>
/// The pool and the deal of the verdict game — "did your friend summon this band, or banish it?".
/// Pure and separated from the database so the rules that decide what a player is asked can be
/// tested without one.
///
/// The pool is the opponent's RESOLVED rites, and only those: <see cref="RiteState.Summoned"/> and
/// <see cref="RiteState.Banished"/> are verdicts, while <see cref="RiteState.Served"/> (dealt, never
/// answered) and <see cref="RiteState.Again"/> (a neutral skip, and what a lost duel side is set to)
/// are not. Asking a player to guess a verdict their friend never gave would be inventing the answer.
/// </summary>
public static class VerdictGamePool
{
    /// <summary>Below this a game is not worth calling one; the empty state is shown instead.</summary>
    public const int MinRounds = 3;

    /// <summary>The longest deal. Five blind rounds is ~4 minutes of listening — a sitting, not a shift.</summary>
    public const int MaxRounds = 5;

    /// <summary>One band in the pool, with the verdict its owner actually gave it.</summary>
    public record Candidate(Guid ArtistId, RiteState Verdict);

    /// <summary>
    /// Whether a pool of resolved rites can be dealt from, before audibility is considered. Both
    /// verdicts must be present: a deal drawn from an all-summoned pool has one possible answer, and
    /// a quiz whose every answer is the same word measures nothing about the player. Refusing is
    /// honest; dealing it would be a coin with two heads.
    /// </summary>
    public static VerdictGameBlocker Check(IReadOnlyCollection<Candidate> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (pool.Count < MinRounds)
        {
            return VerdictGameBlocker.TooFewVerdicts;
        }

        if (!pool.Any(c => c.Verdict == RiteState.Summoned))
        {
            return VerdictGameBlocker.NoSummons;
        }

        if (!pool.Any(c => c.Verdict == RiteState.Banished))
        {
            return VerdictGameBlocker.NoBanishments;
        }

        return VerdictGameBlocker.None;
    }

    /// <summary>How many rounds a pool of this size is dealt: everything it has, capped.</summary>
    public static int RoundsFor(int poolSize)
    {
        return Math.Min(poolSize, MaxRounds);
    }

    /// <summary>
    /// Deals the rounds from the audible candidates of each verdict, already shuffled by the caller.
    /// ONE of each verdict is guaranteed, then the rest is filled at random from what remains.
    ///
    /// The guarantee is deliberate. A purely random draw from a lopsided pool — and real pools are
    /// lopsided, because people summon far more than they banish — produces an all-summoned deal
    /// most of the time, where "always say summoned" scores full marks and the player learns nothing
    /// about their friend. The cost is a small, admitted leak: a player who knows this rule knows at
    /// least one round of every game is a banishment. That is a better trade than a game that
    /// silently stops testing anything.
    ///
    /// The result is shuffled again at the end so the guaranteed pair does not sit at a fixed
    /// position — without it, round 0 would always be a summon and round 1 always a banishment, which
    /// would leak far more than the rule itself.
    /// </summary>
    public static IReadOnlyList<Candidate> Deal(
        IReadOnlyList<Candidate> audibleSummons,
        IReadOnlyList<Candidate> audibleBanishments,
        int rounds,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(audibleSummons);
        ArgumentNullException.ThrowIfNull(audibleBanishments);
        ArgumentNullException.ThrowIfNull(rng);

        if (audibleSummons.Count == 0 || audibleBanishments.Count == 0 || rounds < 2)
        {
            return [];
        }

        List<Candidate> dealt = [audibleSummons[0], audibleBanishments[0]];

        List<Candidate> rest = audibleSummons
            .Skip(1)
            .Concat(audibleBanishments.Skip(1))
            .ToList();

        dealt.AddRange(Shuffle(rest, rng).Take(rounds - 2));

        return Shuffle(dealt, rng);
    }

    /// <summary>A Fisher-Yates shuffle onto a new list, leaving the caller's order untouched.</summary>
    public static List<T> Shuffle<T>(IEnumerable<T> items, Random rng)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rng);

        List<T> copy = items.ToList();

        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }
}
