using Grimoire.Library.Models;

namespace Grimoire.Server.Services;

/// <summary>
/// Why a guess-the-band game cannot be dealt. Every value is a fact about the player's OWN grimoire,
/// and each maps to its own honest sentence in the front — "you have summoned almost nothing" is a
/// different fact from "nothing you summoned can be played right now", and telling them apart is the
/// point (R2: the app degrades, it does not break, and it never fakes a round to fill a screen).
/// </summary>
public enum GuessGameBlocker
{
    /// <summary>Playable.</summary>
    None,

    /// <summary>Fewer summons than a game needs. This game is your grimoire, so an empty grimoire is no game.</summary>
    TooFewSummons,

    /// <summary>
    /// Multiple choice needs four distinct bands to put on screen and the grimoire has fewer. Hard is
    /// still playable at that size, which is why this blocker is per-difficulty rather than global.
    /// </summary>
    NotEnoughChoices,

    /// <summary>The summons are there but too few of the bands can be made to sound (D25/D40).</summary>
    NotEnoughAudible,
}

/// <summary>
/// The pool, the deal and the arithmetic of "guess the band" (D67) — pure and away from the database,
/// so the rules that decide what a player is asked, what they are offered and what it is worth can be
/// tested without one.
///
/// <para>
/// <b>The pool is the player's own summons, and that bound is the game.</b> A general "name this band"
/// quiz was refused twice (D43, D66): you can only name what you already know, so it rewards whoever
/// arrived knowing the canon — precisely inverting the Ranks pillar, in an app whose pool is 31 752
/// Nameless bands whose own biographies mostly do not exist. Over your own summons the question
/// changes sides: you already proved you love this band, blind, with no name attached. Do you know who
/// it is? That one is worth asking, and it cannot be won by having read more magazines.
/// </para>
/// <para>
/// <b>Served, Again and Banished are all out.</b> Only <see cref="RiteState.Summoned"/> is a band you
/// chose. A banishment is a band you rejected in 45 seconds, and asking its name would test nothing —
/// worse, it would quietly turn the game back into the trivia quiz it exists instead of.
/// </para>
/// </summary>
public static class GuessGamePool
{
    /// <summary>Below this it is not a game; the honest empty state is shown instead.</summary>
    public const int MinRounds = 3;

    /// <summary>The longest deal. Five blind rounds is a sitting, not a shift — the same as the verdict game.</summary>
    public const int MaxRounds = 5;

    /// <summary>
    /// Names on screen in <see cref="GameDifficulty.Normal"/>: the answer and three decoys. Four keeps
    /// the blind baseline at one in four, which the Hard multiplier is calibrated against.
    /// </summary>
    public const int ChoiceCount = 4;

    /// <summary>One band in the player's grimoire, with the vector the decoys are chosen by.</summary>
    /// <param name="ArtistId">The band.</param>
    /// <param name="Name">Its name — the answer in Hard, and a choice in Normal.</param>
    public record Candidate(Guid ArtistId, string Name);

    /// <summary>
    /// Whether a grimoire of this size can be dealt from, before audibility is considered. Normal
    /// needs four bands (one answer plus three decoys) where Hard needs only the rounds themselves:
    /// with three summons the multiple choice would have to show two names and become a coin flip, so
    /// it refuses and says why rather than quietly shrinking into a worse game.
    /// </summary>
    public static GuessGameBlocker Check(int summonCount, GameDifficulty difficulty)
    {
        if (summonCount < MinRounds)
        {
            return GuessGameBlocker.TooFewSummons;
        }

        if (difficulty == GameDifficulty.Normal && summonCount < ChoiceCount)
        {
            return GuessGameBlocker.NotEnoughChoices;
        }

        return GuessGameBlocker.None;
    }

    /// <summary>How many rounds a grimoire of this size is dealt: everything it has, capped.</summary>
    public static int RoundsFor(int poolSize)
    {
        return Math.Min(poolSize, MaxRounds);
    }

    /// <summary>
    /// What one correct round is worth. Hard is worth three because the baselines are not comparable:
    /// four names on screen means a player who knows nothing still scores one in four by pressing
    /// anything, while a blank field pays out essentially nothing to a player who cannot name the band.
    /// Three is the honest ratio between "free" and "not free" here, rounded to something a person can
    /// hold in their head — it is a stated exchange rate, not a measurement, and it lives in one place
    /// so that changing it is one edit and not a hunt.
    /// </summary>
    public static int PointsPerRound(GameDifficulty difficulty)
    {
        return difficulty == GameDifficulty.Hard ? 3 : 1;
    }

    /// <summary>The score in points: right rounds times what a round of this difficulty is worth.</summary>
    public static int Points(int correct, GameDifficulty difficulty)
    {
        return correct * PointsPerRound(difficulty);
    }

    /// <summary>
    /// The names offered for one round: the answer and up to <see cref="ChoiceCount"/> - 1 decoys,
    /// shuffled so that the answer's position carries no information.
    ///
    /// <para>
    /// <b>Deterministic on purpose, and this is load-bearing.</b> The choices are not stored anywhere —
    /// there is no column for them — so they are recomputed every time the round is read. If that
    /// recomputation could shift, a player could reload the game twice and intersect the two draws:
    /// the answer is the one name that must appear in both. So the shuffle is a pure function of the
    /// round's id, and the decoys are drawn from a pool that cannot move under it. Both halves matter;
    /// either one alone leaks.
    /// </para>
    /// <para>
    /// The order is by a hash of (round id, artist id) rather than a seeded <c>Random</c>: it needs no
    /// state, it cannot drift if a runtime changes how a seeded generator walks, and — the part that
    /// matters — the answer is hashed by exactly the same rule as the decoys, so nothing about where it
    /// lands is a function of its being the answer.
    /// </para>
    /// </summary>
    /// <param name="roundId">The round. The only source of the ordering, and stable for the round's life.</param>
    /// <param name="answer">The band actually playing.</param>
    /// <param name="decoys">The wrong names, already chosen by the caller (nearest neighbours in the map).</param>
    public static IReadOnlyList<Candidate> Choices(Guid roundId, Candidate answer, IEnumerable<Candidate> decoys)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(decoys);

        List<Candidate> all = [answer];

        foreach (Candidate decoy in decoys)
        {
            if (decoy.ArtistId == answer.ArtistId)
            {
                // The answer offered twice would be two right buttons — and a player who noticed the
                // repeat would have found the answer by reading the screen. The caller is supposed to
                // have excluded it; this is the belt to that pair of braces.
                continue;
            }

            if (all.Count >= ChoiceCount)
            {
                break;
            }

            all.Add(decoy);
        }

        return all
            .OrderBy(c => Mix(roundId, c.ArtistId))
            .ThenBy(c => c.ArtistId)
            .ToList();
    }

    /// <summary>
    /// Deterministically orders bands for a round when there is no map to order them by — an artist
    /// with no embedding, which the pool should not contain but honestly might (Invariant 5: a source
    /// may always be missing, and the view must not break when it is). The decoys are then merely
    /// arbitrary instead of near, which is an easier round, not a broken one.
    /// </summary>
    public static IReadOnlyList<Candidate> ArbitraryOrder(Guid roundId, IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderBy(c => Mix(roundId, c.ArtistId))
            .ThenBy(c => c.ArtistId)
            .ToList();
    }

    /// <summary>
    /// A stable 32-bit mix of two GUIDs — FNV-1a over their bytes. Hand-rolled rather than
    /// <c>GetHashCode</c> because this value decides what a player sees and must therefore mean the
    /// same thing in every process, on every machine, after every upgrade: a framework hash promises
    /// none of that, and the day it changed, every game in flight would silently re-shuffle.
    /// </summary>
    public static uint Mix(Guid a, Guid b)
    {
        Span<byte> bytes = stackalloc byte[32];
        a.TryWriteBytes(bytes[..16]);
        b.TryWriteBytes(bytes[16..]);

        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        uint hash = offsetBasis;

        foreach (byte value in bytes)
        {
            hash = unchecked((hash ^ value) * prime);
        }

        return hash;
    }
}
