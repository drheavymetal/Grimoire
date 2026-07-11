using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The rarity term (SPEC §6) and its weighted-within-ring pick. These bite on the part that keeps
/// the motor honest: a null listener count is "unknown", not "infinitely rare", so it must weigh
/// neutrally and never take over the selection.
/// </summary>
public class RaritySelectorTests
{
    private const double W = 0.15;

    // -----------------------------------------------------------------------
    // The rarity term
    // -----------------------------------------------------------------------

    [Fact]
    public void RarityTerm_Null_IsNeutralZero()
    {
        // A null listener count must be a NEUTRAL 0, not a huge value. If this ever returned
        // something large, the dark tail without Last.fm data would win every draw — the exact
        // failure this test guards against.
        Assert.Equal(0.0, RaritySelector.RarityTerm(null, W));
    }

    [Fact]
    public void RarityTerm_Null_EqualsAMillionListeners()
    {
        // ln(1e6 / 1e6) = 0, so an unknown band weighs exactly like a band right at 1e6 listeners:
        // neutral, the middle of the road, never the rarest.
        Assert.Equal(RaritySelector.RarityTerm(1_000_000, W), RaritySelector.RarityTerm(null, W), 9);
    }

    [Fact]
    public void RarityTerm_RarerBand_ScoresStrictlyHigher()
    {
        double nameless = RaritySelector.RarityTerm(400, W);   // < 500 listeners
        double forgotten = RaritySelector.RarityTerm(4_000, W);
        double known = RaritySelector.RarityTerm(2_000_000, W);

        Assert.True(nameless > forgotten, $"nameless {nameless} must beat forgotten {forgotten}");
        Assert.True(forgotten > 0.0, "a genuinely rare band must score positive");
        Assert.True(known < 0.0, "a mega-popular band (>1e6) must score negative — inverse popularity");
    }

    [Fact]
    public void RarityTerm_NullIsRarerThanTheMostPopular_ButNotRarerThanTheRare()
    {
        // The crux, stated as an ordering: null (0) sits ABOVE a >1e6-listener band (negative) yet
        // BELOW any genuinely rare band (positive). Unknown is neutral, not rarest.
        double nameless = RaritySelector.RarityTerm(400, W);
        double nullTerm = RaritySelector.RarityTerm(null, W);
        double mega = RaritySelector.RarityTerm(5_000_000, W);

        Assert.True(nameless > nullTerm, "a real rare band must outrank an unknown one");
        Assert.True(nullTerm > mega, "an unknown band must outrank a mega-popular one");
    }

    [Fact]
    public void RarityTerm_TreatsZeroAsOne_NoDivideByZero()
    {
        // GREATEST(listeners, 1): 0 listeners must not blow up ln, it clamps to 1.
        Assert.Equal(W * System.Math.Log(1_000_000.0), RaritySelector.RarityTerm(0, W), 9);
    }

    [Fact]
    public void RarityTerm_ZeroWeight_DisablesTheTerm()
    {
        Assert.Equal(0.0, RaritySelector.RarityTerm(1, 0.0));
        Assert.Equal(0.0, RaritySelector.RarityTerm(400, 0.0));
    }

    // -----------------------------------------------------------------------
    // The weighted pick
    // -----------------------------------------------------------------------

    [Fact]
    public void SelectIndex_Empty_ReturnsMinusOne()
    {
        Assert.Equal(-1, RaritySelector.SelectIndex([], () => 0.5));
    }

    [Fact]
    public void SelectIndex_WithEqualNoise_PicksTheHighestTerm()
    {
        // A constant draw gives every candidate the same Gumbel noise, so the argmax reduces to the
        // largest rarity term. Index 2 (the rarest) must win. (Invert to expect index 0 and it fails
        // — the bias direction is load-bearing.)
        double[] terms = [RaritySelector.RarityTerm(2_000_000, W), 0.0, RaritySelector.RarityTerm(300, W)];

        Assert.Equal(2, RaritySelector.SelectIndex(terms, () => 0.5));
    }

    [Fact]
    public void SelectIndex_WithEqualNoise_NullDoesNotBeatARareBand()
    {
        // [nullBand, namelessBand]: with equal noise the nameless band must win. A null must never
        // dominate a genuinely rare one.
        double[] terms = [RaritySelector.RarityTerm(null, W), RaritySelector.RarityTerm(300, W)];

        Assert.Equal(1, RaritySelector.SelectIndex(terms, () => 0.5));
    }

    [Fact]
    public void SelectIndex_AllEqualTerms_IsUniform_RecoveringRandomWithinRing()
    {
        // With every term 0 (e.g. an all-null ring, or weight 0) the pick must be driven purely by
        // the random draw — the D26/D31 random-within-ring behaviour. Feed increasing draws and the
        // last index (largest Gumbel key) wins.
        double[] terms = [0.0, 0.0, 0.0];
        double[] draws = [0.1, 0.5, 0.9];
        int i = 0;

        int chosen = RaritySelector.SelectIndex(terms, () => draws[i++]);

        Assert.Equal(2, chosen); // 0.9 → the largest Gumbel key
    }

    [Fact]
    public void SelectIndex_NullBandWeighsLikeAMegaPopularOne_NotLikeARareOne()
    {
        // A frequency check with a SEEDED rng (deterministic, not flaky). Over many draws a Nameless
        // band must be picked far more often than a null band, and the null band must land near a
        // mega-popular band — proving null is neutral, not rarest.
        //   index 0: Nameless (400 listeners)   index 1: null   index 2: mega (5,000,000)
        double[] terms =
        [
            RaritySelector.RarityTerm(400, W),
            RaritySelector.RarityTerm(null, W),
            RaritySelector.RarityTerm(5_000_000, W),
        ];

        var rng = new System.Random(1234);
        int[] counts = new int[3];

        for (int n = 0; n < 30_000; n++)
        {
            counts[RaritySelector.SelectIndex(terms, rng.NextDouble)]++;
        }

        Assert.True(counts[0] > counts[1], $"nameless {counts[0]} must be picked more than null {counts[1]}");
        Assert.True(counts[1] > counts[2], $"null {counts[1]} must be picked more than mega {counts[2]}");

        // The null band lands closer to the mega band than to the nameless one: it is not "rarest".
        Assert.True(counts[1] < counts[0] / 2, $"null {counts[1]} must be nowhere near nameless {counts[0]}");
    }
}
