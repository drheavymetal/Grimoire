using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The rules that decide whether the verdict game may be dealt, and what it deals. Pure — no
/// database, no HTTP — so the honesty rules are pinned independently of the plumbing that feeds them.
/// The exclusion of Served/Again from the pool is enforced in SQL and is covered end-to-end by
/// <see cref="VerdictGameTests"/>; here we hold the decisions taken once the pool is in hand.
/// </summary>
public class VerdictGamePoolTests
{
    private static VerdictGamePool.Candidate Summoned()
    {
        return new VerdictGamePool.Candidate(Guid.NewGuid(), RiteState.Summoned);
    }

    private static VerdictGamePool.Candidate Banished()
    {
        return new VerdictGamePool.Candidate(Guid.NewGuid(), RiteState.Banished);
    }

    private static List<VerdictGamePool.Candidate> Pool(int summons, int banishments)
    {
        List<VerdictGamePool.Candidate> pool = [];

        for (int i = 0; i < summons; i++)
        {
            pool.Add(Summoned());
        }

        for (int i = 0; i < banishments; i++)
        {
            pool.Add(Banished());
        }

        return pool;
    }

    [Fact]
    public void Check_Passes_WithBothVerdictsAndEnoughOfThem()
    {
        Assert.Equal(VerdictGameBlocker.None, VerdictGamePool.Check(Pool(2, 1)));
    }

    /// <summary>
    /// The data reality this ships into: a friend who has barely played. Two verdicts is not a game,
    /// and saying so is the designed empty state — not an error, not a blank screen.
    /// </summary>
    [Fact]
    public void Check_RefusesAPoolSmallerThanTheMinimum()
    {
        Assert.Equal(VerdictGameBlocker.TooFewVerdicts, VerdictGamePool.Check(Pool(1, 1)));
    }

    /// <summary>
    /// The degeneracy that matters most: everybody summons far more than they banish, so an
    /// all-summoned pool is the common case. A quiz whose every answer is "summoned" measures
    /// nothing, and dealing it would be a coin with two heads dressed as a test of your friend's ear.
    /// </summary>
    [Fact]
    public void Check_RefusesAPoolWithNoBanishments()
    {
        Assert.Equal(VerdictGameBlocker.NoBanishments, VerdictGamePool.Check(Pool(8, 0)));
    }

    [Fact]
    public void Check_RefusesAPoolWithNoSummons()
    {
        Assert.Equal(VerdictGameBlocker.NoSummons, VerdictGamePool.Check(Pool(0, 8)));
    }

    /// <summary>Too small AND one-sided reports the size first: the friend needs to play, not to banish.</summary>
    [Fact]
    public void Check_ReportsSizeBeforeOneSidedness()
    {
        Assert.Equal(VerdictGameBlocker.TooFewVerdicts, VerdictGamePool.Check(Pool(2, 0)));
    }

    [Fact]
    public void RoundsFor_TakesEverythingUpToTheCap()
    {
        Assert.Equal(3, VerdictGamePool.RoundsFor(3));
        Assert.Equal(VerdictGamePool.MaxRounds, VerdictGamePool.RoundsFor(50));
    }

    /// <summary>
    /// The guarantee that keeps the game a test: a lopsided pool must still deal at least one of each
    /// verdict, or "always say summoned" scores full marks. Run many times because the deal is random
    /// — one pass could pass by luck.
    /// </summary>
    [Fact]
    public void Deal_AlwaysIncludesBothVerdicts_EvenFromALopsidedPool()
    {
        List<VerdictGamePool.Candidate> summons = Pool(20, 0);
        List<VerdictGamePool.Candidate> banishments = Pool(0, 1);
        Random rng = new(26);

        for (int i = 0; i < 200; i++)
        {
            IReadOnlyList<VerdictGamePool.Candidate> dealt =
                VerdictGamePool.Deal(summons, banishments, VerdictGamePool.MaxRounds, rng);

            Assert.Contains(dealt, c => c.Verdict == RiteState.Summoned);
            Assert.Contains(dealt, c => c.Verdict == RiteState.Banished);
        }
    }

    [Fact]
    public void Deal_DealsExactlyTheRoundsAsked()
    {
        IReadOnlyList<VerdictGamePool.Candidate> dealt =
            VerdictGamePool.Deal(Pool(10, 0), Pool(0, 10), 5, new Random(26));

        Assert.Equal(5, dealt.Count);
    }

    [Fact]
    public void Deal_NeverRepeatsABand()
    {
        List<VerdictGamePool.Candidate> summons = Pool(10, 0);
        List<VerdictGamePool.Candidate> banishments = Pool(0, 10);
        Random rng = new(26);

        for (int i = 0; i < 100; i++)
        {
            IReadOnlyList<VerdictGamePool.Candidate> dealt = VerdictGamePool.Deal(summons, banishments, 5, rng);

            Assert.Equal(dealt.Count, dealt.Select(c => c.ArtistId).Distinct().Count());
        }
    }

    /// <summary>
    /// The guaranteed pair must not sit at a fixed position. Without the final shuffle round 0 would
    /// always be a summon and round 1 always a banishment — a far bigger leak than the guarantee, and
    /// one a player would notice in two games.
    /// </summary>
    [Fact]
    public void Deal_DoesNotPinTheGuaranteedPairToTheFirstTwoRounds()
    {
        List<VerdictGamePool.Candidate> summons = Pool(10, 0);
        List<VerdictGamePool.Candidate> banishments = Pool(0, 10);
        Random rng = new(26);

        int banishmentFirst = 0;

        for (int i = 0; i < 200; i++)
        {
            IReadOnlyList<VerdictGamePool.Candidate> dealt = VerdictGamePool.Deal(summons, banishments, 5, rng);

            if (dealt[0].Verdict == RiteState.Banished)
            {
                banishmentFirst++;
            }
        }

        // A pinned deal would put a summon first every single time.
        Assert.True(banishmentFirst > 0, "Round 0 was a summon in all 200 deals — the deal order looks pinned.");
    }

    [Fact]
    public void Deal_YieldsNothing_WhenAVerdictIsMissingEntirely()
    {
        Assert.Empty(VerdictGamePool.Deal(Pool(5, 0), [], 5, new Random(26)));
        Assert.Empty(VerdictGamePool.Deal([], Pool(0, 5), 5, new Random(26)));
    }

    [Fact]
    public void Shuffle_KeepsEveryItemAndLeavesTheSourceAlone()
    {
        List<int> source = Enumerable.Range(0, 50).ToList();
        List<int> shuffled = VerdictGamePool.Shuffle(source, new Random(26));

        Assert.Equal(Enumerable.Range(0, 50), source);
        Assert.Equal(source.OrderBy(x => x), shuffled.OrderBy(x => x));
    }
}
