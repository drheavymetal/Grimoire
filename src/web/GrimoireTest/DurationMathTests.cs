using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The duration helpers (B5 tracklist, C7 the axis). These bite the honesty boundary: a null length
/// is an absence, never a zero — it renders as an em dash and never drags an average down.
/// </summary>
public class DurationMathTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1000, "0:01")]
    [InlineData(59000, "0:59")]
    [InlineData(60000, "1:00")]
    [InlineData(251280, "4:11")]   // Darkthrone, "Cromlech" — a real row from the live base.
    [InlineData(3600000, "1:00:00")]
    [InlineData(3661000, "1:01:01")]
    public void FormatLength_RendersMinutesAndSeconds(int lengthMs, string expected)
    {
        Assert.Equal(expected, DurationMath.FormatLength(lengthMs));
    }

    [Fact]
    public void FormatLength_NullIsAnEmDashNotZero()
    {
        Assert.Equal("—", DurationMath.FormatLength(null));
    }

    [Fact]
    public void FormatLength_NegativeIsTreatedAsMissing()
    {
        Assert.Equal("—", DurationMath.FormatLength(-1));
    }

    [Fact]
    public void AverageMs_ExcludesNullLengthsFromBothSumAndCount()
    {
        // Two timed tracks (100, 300) and one untimed: the mean is 200, NOT 400/3 ≈ 133.
        double? average = DurationMath.AverageMs([100, null, 300]);
        Assert.Equal(200.0, average);
    }

    [Fact]
    public void AverageMs_AllNullIsNullNotZero()
    {
        Assert.Null(DurationMath.AverageMs([null, null]));
    }

    [Fact]
    public void AverageMs_EmptyIsNull()
    {
        Assert.Null(DurationMath.AverageMs([]));
    }
}
