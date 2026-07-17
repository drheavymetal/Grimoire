using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The rules of "guess the band" (D67) that decide what a player is asked, what they are offered and
/// what it is worth — held to their promises without a database.
///
/// The load-bearing one is the multiple choice. Those four names are NOT stored anywhere (there is no
/// column for them), so they are recomputed on every read of the round; if that recomputation could
/// shift, the game would be winnable by reloading it twice and taking the name in both draws.
/// </summary>
public class GuessGamePoolTests
{
    // -----------------------------------------------------------------------
    // The gate: what a grimoire must have before it is a game
    // -----------------------------------------------------------------------

    /// <summary>An almost-empty grimoire is not a game, at either difficulty. It says so, honestly.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AGrimoireBelowTheMinimum_IsBlocked_AtBothDifficulties(int summons)
    {
        Assert.Equal(GuessGameBlocker.TooFewSummons, GuessGamePool.Check(summons, GameDifficulty.Normal));
        Assert.Equal(GuessGameBlocker.TooFewSummons, GuessGamePool.Check(summons, GameDifficulty.Hard));
    }

    /// <summary>
    /// THE per-difficulty rule. Three summons can make a typed game but not a multiple choice: the
    /// choice would have to show three names, or two, and a coin flip dressed as a quiz measures
    /// nothing. It refuses and says which of the two problems it has, rather than silently shrinking
    /// into a worse game — a blocker per fact is the whole empty-state discipline (R2).
    /// </summary>
    [Fact]
    public void ThreeSummons_PlayHard_ButCannotFillAMultipleChoice()
    {
        Assert.Equal(GuessGameBlocker.NotEnoughChoices, GuessGamePool.Check(3, GameDifficulty.Normal));
        Assert.Equal(GuessGameBlocker.None, GuessGamePool.Check(3, GameDifficulty.Hard));
    }

    /// <summary>Four is where the multiple choice opens: the answer, plus the three decoys it needs.</summary>
    [Fact]
    public void FourSummons_OpenBothDifficulties()
    {
        Assert.Equal(GuessGameBlocker.None, GuessGamePool.Check(4, GameDifficulty.Normal));
        Assert.Equal(GuessGameBlocker.None, GuessGamePool.Check(4, GameDifficulty.Hard));
    }

    /// <summary>A deal takes what the grimoire has, capped — never more rounds than bands.</summary>
    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(40, 5)]
    public void RoundsFor_TakesWhatThereIs_Capped(int pool, int expected)
    {
        Assert.Equal(expected, GuessGamePool.RoundsFor(pool));
    }

    // -----------------------------------------------------------------------
    // Scoring: the two difficulties are not worth the same
    // -----------------------------------------------------------------------

    /// <summary>
    /// Hard pays three times Normal, because the baselines are not comparable: four names on screen
    /// hands a player who knows nothing one round in four for free, and a blank field hands them
    /// nothing. Flip these and the two modes become the same game with different typing.
    /// </summary>
    [Fact]
    public void Hard_IsWorthMoreThanNormal()
    {
        Assert.Equal(1, GuessGamePool.PointsPerRound(GameDifficulty.Normal));
        Assert.Equal(3, GuessGamePool.PointsPerRound(GameDifficulty.Hard));
        Assert.True(GuessGamePool.PointsPerRound(GameDifficulty.Hard) > GuessGamePool.PointsPerRound(GameDifficulty.Normal));
    }

    /// <summary>Points are rounds times the rate — and nothing scores anything.</summary>
    [Theory]
    [InlineData(0, GameDifficulty.Normal, 0)]
    [InlineData(5, GameDifficulty.Normal, 5)]
    [InlineData(0, GameDifficulty.Hard, 0)]
    [InlineData(4, GameDifficulty.Hard, 12)]
    public void Points_AreRoundsTimesTheRate(int correct, GameDifficulty difficulty, int expected)
    {
        Assert.Equal(expected, GuessGamePool.Points(correct, difficulty));
    }

    // -----------------------------------------------------------------------
    // The choices: four names, one true, and nothing that says which
    // -----------------------------------------------------------------------

    /// <summary>The answer is always on the list — a round with no right button is not a round.</summary>
    [Fact]
    public void Choices_AlwaysContainTheAnswer()
    {
        GuessGamePool.Candidate answer = Band(1, "Darkthrone");

        IReadOnlyList<GuessGamePool.Candidate> choices = GuessGamePool.Choices(
            Round(99), answer, [Band(2, "Burzum"), Band(3, "Mayhem"), Band(4, "Emperor")]);

        Assert.Equal(GuessGamePool.ChoiceCount, choices.Count);
        Assert.Contains(choices, c => c.ArtistId == answer.ArtistId);
    }

    /// <summary>
    /// The answer appears ONCE. A decoy list that happens to contain the answer would put two right
    /// buttons on screen — and a player who spotted the repeat would have read the answer off the
    /// screen instead of hearing it. The caller is supposed to exclude it; this is the second guard.
    /// </summary>
    [Fact]
    public void Choices_NeverOfferTheAnswerTwice_EvenIfTheDecoysContainIt()
    {
        GuessGamePool.Candidate answer = Band(1, "Darkthrone");

        IReadOnlyList<GuessGamePool.Candidate> choices = GuessGamePool.Choices(
            Round(7), answer, [Band(1, "Darkthrone"), Band(2, "Burzum"), Band(3, "Mayhem"), Band(4, "Emperor")]);

        Assert.Single(choices, c => c.ArtistId == answer.ArtistId);
        Assert.Equal(choices.Count, choices.Select(c => c.ArtistId).Distinct().Count());
    }

    /// <summary>Never more names than the mode shows, however many decoys the caller pulled.</summary>
    [Fact]
    public void Choices_AreCappedAtTheChoiceCount()
    {
        IReadOnlyList<GuessGamePool.Candidate> choices = GuessGamePool.Choices(
            Round(3),
            Band(1, "Darkthrone"),
            Enumerable.Range(2, 20).Select(i => Band(i, $"Band {i}")));

        Assert.Equal(GuessGamePool.ChoiceCount, choices.Count);
    }

    /// <summary>
    /// A smaller grimoire yields a smaller list rather than an invented name. Nothing is padded: the
    /// controller's gate is what keeps this from reaching a player, and the pure function stays honest
    /// about what it was given (Invariant 5).
    /// </summary>
    [Fact]
    public void Choices_WithTooFewDecoys_ReturnWhatExists_AndInventNothing()
    {
        IReadOnlyList<GuessGamePool.Candidate> choices = GuessGamePool.Choices(
            Round(3), Band(1, "Darkthrone"), [Band(2, "Burzum")]);

        Assert.Equal(2, choices.Count);
    }

    /// <summary>
    /// THE anti-cheat property, at the unit level: the same round yields the same order, for ever. The
    /// choices are not stored, so a resume recomputes them — and two draws that disagreed could be
    /// intersected, because the one name in both is the answer. Reordering the decoys the caller hands
    /// in must not move anything either: the order in is not a fact about the round.
    /// </summary>
    [Fact]
    public void Choices_AreStable_ForTheSameRound_WhateverOrderTheDecoysArriveIn()
    {
        Guid round = Round(42);
        GuessGamePool.Candidate answer = Band(1, "Darkthrone");
        List<GuessGamePool.Candidate> decoys = [Band(2, "Burzum"), Band(3, "Mayhem"), Band(4, "Emperor")];

        List<Guid> first = GuessGamePool.Choices(round, answer, decoys).Select(c => c.ArtistId).ToList();
        List<Guid> again = GuessGamePool.Choices(round, answer, decoys).Select(c => c.ArtistId).ToList();
        List<Guid> reversed = GuessGamePool.Choices(round, answer, Enumerable.Reverse(decoys)).Select(c => c.ArtistId).ToList();

        Assert.Equal(first, again);
        Assert.Equal(first, reversed);
    }

    /// <summary>Different rounds shuffle differently — otherwise the position would be learnable once and reused.</summary>
    [Fact]
    public void Choices_AreOrderedDifferently_AcrossRounds()
    {
        GuessGamePool.Candidate answer = Band(1, "Darkthrone");
        List<GuessGamePool.Candidate> decoys = [Band(2, "Burzum"), Band(3, "Mayhem"), Band(4, "Emperor")];

        HashSet<string> orders = [];

        for (int i = 0; i < 50; i++)
        {
            orders.Add(string.Join(",", GuessGamePool.Choices(Round(i), answer, decoys).Select(c => c.Name)));
        }

        Assert.True(orders.Count > 1, "Every round produced the same order: the shuffle is not a function of the round.");
    }

    /// <summary>
    /// THE one that matters most. The answer must land in every position, at roughly the same rate: if
    /// it always sat first — or merely favoured a slot — the game would be won by pressing that button,
    /// and it would be won silently, by a payload of exactly the right shape.
    ///
    /// Deterministic despite testing a shuffle: the round ids are generated from a counter, so this
    /// either passes for ever or fails for ever, and it can never flake its way past a real bias.
    /// </summary>
    [Fact]
    public void TheAnswer_LandsInEveryPosition_AtRoughlyTheSameRate()
    {
        GuessGamePool.Candidate answer = Band(1, "Darkthrone");
        List<GuessGamePool.Candidate> decoys = [Band(2, "Burzum"), Band(3, "Mayhem"), Band(4, "Emperor")];

        int[] positions = new int[GuessGamePool.ChoiceCount];
        const int draws = 4000;

        for (int i = 0; i < draws; i++)
        {
            IReadOnlyList<GuessGamePool.Candidate> choices = GuessGamePool.Choices(Round(i), answer, decoys);
            positions[choices.Select((c, index) => (c, index)).First(x => x.c.ArtistId == answer.ArtistId).index]++;
        }

        // Uniform would be 1000 each. A band this wide catches a real bias (a fixed slot, a two-slot
        // preference) while leaving the ordinary lumpiness of a hash alone.
        Assert.All(positions, count => Assert.InRange(count, draws / 4 * 3 / 4, draws / 4 * 5 / 4));
    }

    /// <summary>
    /// The decoys must not be sortable into the answer either: with the answer excluded, the same hash
    /// orders the rest, so nothing about the ordering is a function of being correct. Here the "answer"
    /// is swapped for a decoy and the distribution must not change character.
    /// </summary>
    [Fact]
    public void ThePosition_DependsOnTheRoundAndTheBand_NotOnBeingTheAnswer()
    {
        List<GuessGamePool.Candidate> bands =
            [Band(1, "Darkthrone"), Band(2, "Burzum"), Band(3, "Mayhem"), Band(4, "Emperor")];

        for (int i = 0; i < 200; i++)
        {
            Guid round = Round(i);

            // Dealt with band 0 as the answer, and again with band 1 as the answer: the ORDER of the
            // four names must be identical, because it is a fact about the round, not about which of
            // them happens to be right.
            List<Guid> asFirst = GuessGamePool.Choices(round, bands[0], bands.Skip(1)).Select(c => c.ArtistId).ToList();
            List<Guid> asSecond = GuessGamePool.Choices(round, bands[1], bands.Where(b => b != bands[1])).Select(c => c.ArtistId).ToList();

            Assert.Equal(asFirst, asSecond);
        }
    }

    /// <summary>
    /// The fallback ordering, for a band with no embedding. It must still be deterministic: an artist
    /// without a vector is a degraded round (arbitrary decoys instead of near ones), never a leaking one.
    /// </summary>
    [Fact]
    public void ArbitraryOrder_IsStable_ForTheSameRound()
    {
        List<GuessGamePool.Candidate> bands =
            [Band(1, "Darkthrone"), Band(2, "Burzum"), Band(3, "Mayhem"), Band(4, "Emperor")];

        List<Guid> first = GuessGamePool.ArbitraryOrder(Round(5), bands).Select(c => c.ArtistId).ToList();
        List<Guid> again = GuessGamePool.ArbitraryOrder(Round(5), Enumerable.Reverse(bands)).Select(c => c.ArtistId).ToList();

        Assert.Equal(first, again);
    }

    /// <summary>
    /// The hash itself: same inputs, same answer, and the two arguments are not interchangeable. It is
    /// hand-rolled precisely so that this is a promise the codebase makes rather than one a framework
    /// happens to keep this year — a shuffle that changed on a runtime upgrade would silently redeal
    /// every game in flight.
    /// </summary>
    [Fact]
    public void Mix_IsDeterministic_AndOrderSensitive()
    {
        Guid a = Round(1);
        Guid b = Round(2);

        Assert.Equal(GuessGamePool.Mix(a, b), GuessGamePool.Mix(a, b));
        Assert.NotEqual(GuessGamePool.Mix(a, b), GuessGamePool.Mix(b, a));
    }

    // -- helpers --------------------------------------------------------------

    /// <summary>A band with a GUID derived from a counter, so every case here is reproducible.</summary>
    private static GuessGamePool.Candidate Band(int seed, string name)
    {
        return new GuessGamePool.Candidate(Deterministic(seed, 0xB0), name);
    }

    private static Guid Round(int seed)
    {
        return Deterministic(seed, 0x81);
    }

    private static Guid Deterministic(int seed, byte tag)
    {
        byte[] bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes, seed);
        bytes[15] = tag;

        return new Guid(bytes);
    }
}
