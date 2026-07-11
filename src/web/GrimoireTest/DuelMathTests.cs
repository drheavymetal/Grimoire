using Grimoire.Library.Services;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The pairwise-preference taste update behind the blind duel (feature C2, DECISIONS D16). These
/// bite: they assert DIRECTION — the taste must move toward the winner and away from the loser — and
/// that the duel separates the two more than summoning the winner alone would (which is the whole
/// reason a preference teaches more than a like). Flip an assertion and the test fails.
/// </summary>
public class DuelMathTests
{
    [Fact]
    public void ApplyDuel_MovesTasteTowardTheWinner()
    {
        float[] taste = [1f, 0f];
        float[] winner = [0f, 1f];
        float[] loser = [1f, 0f];

        double before = VectorMath.CosineDistance(taste, winner);
        float[] after = DuelMath.ApplyDuel(taste, winner, loser);
        double now = VectorMath.CosineDistance(after, winner);

        // Closer to the winner than it was. (Invert to "now > before" and it fails — the pull has a
        // definite direction.)
        Assert.True(now < before, $"duel should reduce distance to the winner: before={before}, after={now}");
    }

    [Fact]
    public void ApplyDuel_MovesTasteAwayFromTheLoser()
    {
        float[] taste = [1f, 0f];
        float[] winner = [0f, 1f];
        float[] loser = [1f, 0f];

        double before = VectorMath.CosineDistance(taste, loser);
        float[] after = DuelMath.ApplyDuel(taste, winner, loser);
        double now = VectorMath.CosineDistance(after, loser);

        // Farther from the loser than it was. The loser started ON the taste (distance 0); after the
        // duel the taste has been pushed off it. Invert to "now < before" and it fails.
        Assert.True(now > before, $"duel should increase distance to the loser: before={before}, after={now}");
    }

    [Fact]
    public void ApplyDuel_SeparatesWinnerFromLoser_MoreThanSummoningTheWinnerAlone()
    {
        // A duel is worth more than a single like (DECISIONS D16): the preference margin
        // d(loser) − d(winner) must widen MORE under a duel than under a lone summon of the winner,
        // because the duel also pushes off the loser.
        float[] taste = [1f, 0f];
        float[] winner = [0f, 1f];
        float[] loser = [1f, 0f];

        float[] duel = DuelMath.ApplyDuel(taste, winner, loser, 0.25, 0.10);
        float[] likeOnly = TasteMath.ApplySummon(taste, winner, 0.25);

        double duelMargin = VectorMath.CosineDistance(duel, loser) - VectorMath.CosineDistance(duel, winner);
        double likeMargin = VectorMath.CosineDistance(likeOnly, loser) - VectorMath.CosineDistance(likeOnly, winner);

        Assert.True(
            duelMargin > likeMargin,
            $"a duel must separate winner from loser more than a like: duel={duelMargin}, like={likeMargin}");
    }

    [Fact]
    public void ApplyDuel_WithNullTaste_StartsFromTheWinner_ThenPushesOffTheLoser()
    {
        float[] winner = [0f, 1f];
        float[] loser = [1f, 0f];

        float[] after = DuelMath.ApplyDuel(null, winner, loser);

        // With no prior taste, the result sits near the winner and away from the loser.
        Assert.True(
            VectorMath.CosineDistance(after, winner) < VectorMath.CosineDistance(after, loser),
            "a first duel with no seed should land closer to the winner than the loser");
    }

    [Fact]
    public void ApplyDuel_WinnerWeight_ControlsHowFarTheTasteMoves()
    {
        float[] taste = [1f, 0f];
        float[] winner = [0f, 1f];
        float[] loser = [1f, 0f];

        float[] gentle = DuelMath.ApplyDuel(taste, winner, loser, 0.10, 0.10);
        float[] strong = DuelMath.ApplyDuel(taste, winner, loser, 0.50, 0.10);

        Assert.True(
            VectorMath.CosineDistance(strong, winner) < VectorMath.CosineDistance(gentle, winner),
            "a larger winner weight must move the taste further toward the winner");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ApplyDuel_RejectsWinnerWeightOutsideZeroToOne(double weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DuelMath.ApplyDuel([1f, 0f], [0f, 1f], [1f, 0f], weight, 0.10));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.1)]
    public void ApplyDuel_RejectsLoserWeightOutsideZeroToOne(double weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DuelMath.ApplyDuel([1f, 0f], [0f, 1f], [1f, 0f], 0.25, weight));
    }

    [Fact]
    public void ApplyDuel_RejectsMismatchedDimensions()
    {
        Assert.Throws<ArgumentException>(() => DuelMath.ApplyDuel([1f, 0f], [0f, 1f, 0f], [1f, 0f]));
        Assert.Throws<ArgumentException>(() => DuelMath.ApplyDuel([1f, 0f, 0f], [0f, 1f], [1f, 0f]));
    }
}
