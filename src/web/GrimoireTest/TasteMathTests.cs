using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The taste/repulsion arithmetic behind Summon and Banish (DECISIONS D4, D15, D26). These bite:
/// they assert direction, not just non-throwing — summoning must pull the taste toward the band,
/// banishing must move the repulsion toward it. Flip the assertion and the test fails.
/// </summary>
public class TasteMathTests
{
    [Fact]
    public void Seed_IsTheMeanOfTheChosenEmbeddings()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 2f, 0f];

        float[] seed = TasteMath.Seed([a, b]);

        Assert.Equal(0.5f, seed[0], 5);
        Assert.Equal(1.0f, seed[1], 5);
        Assert.Equal(0.0f, seed[2], 5);
    }

    [Fact]
    public void ApplySummon_MovesTasteTowardTheSummonedArtist()
    {
        float[] taste = [1f, 0f];
        float[] artist = [0f, 1f];

        double before = VectorMath.CosineDistance(taste, artist);
        float[] after = TasteMath.ApplySummon(taste, artist);
        double now = VectorMath.CosineDistance(after, artist);

        // The taste is now closer to the summoned band than it was. (Invert to "now > before"
        // and this fails — the moving average has a definite direction.)
        Assert.True(now < before, $"summon should reduce distance: before={before}, after={now}");
    }

    [Fact]
    public void ApplySummon_WithNullTaste_BecomesTheArtist()
    {
        float[] artist = [0.3f, -0.7f, 0.1f];

        float[] after = TasteMath.ApplySummon(null, artist);

        Assert.Equal(artist, after);
        Assert.NotSame(artist, after);
    }

    [Fact]
    public void ApplyBanish_WithNullRepulsion_SeedsFromTheBanishedArtist()
    {
        float[] artist = [0.2f, 0.9f];

        float[] repulsion = TasteMath.ApplyBanish(null, artist);

        Assert.Equal(artist, repulsion);
        Assert.NotSame(artist, repulsion);
    }

    [Fact]
    public void ApplyBanish_MovesRepulsionTowardTheBanishedArtist()
    {
        float[] repulsion = [1f, 0f];
        float[] artist = [0f, 1f];

        double before = VectorMath.CosineDistance(repulsion, artist);
        float[] after = TasteMath.ApplyBanish(repulsion, artist);
        double now = VectorMath.CosineDistance(after, artist);

        Assert.True(now < before, $"banish should pull repulsion toward the band: before={before}, after={now}");
    }

    [Fact]
    public void ApplySummon_Decay_ControlsHowFarTheTasteMoves()
    {
        float[] taste = [1f, 0f];
        float[] artist = [0f, 1f];

        float[] gentle = TasteMath.ApplySummon(taste, artist, 0.10);
        float[] strong = TasteMath.ApplySummon(taste, artist, 0.50);

        // A larger decay moves further toward the band.
        Assert.True(
            VectorMath.CosineDistance(strong, artist) < VectorMath.CosineDistance(gentle, artist));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ApplySummon_RejectsDecayOutsideZeroToOne(double decay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TasteMath.ApplySummon([1f, 0f], [0f, 1f], decay));
    }
}
