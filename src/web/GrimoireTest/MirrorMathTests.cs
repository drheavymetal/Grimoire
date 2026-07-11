using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The mirror (feature C20). These bite on the percentage that is the whole point: the fraction of
/// banished bands carrying the user's favourite genre, and the empty state when there is nothing yet.
/// </summary>
public class MirrorMathTests
{
    private static IReadOnlyList<IReadOnlyList<string>> Bands(params string[][] tagSets)
    {
        return tagSets.Select(t => (IReadOnlyList<string>)t).ToList();
    }

    [Fact]
    public void Compute_FractionOfBanishedInFavouriteGenre()
    {
        // Favourite (from summoned) is "black metal". Of five banished bands, three carry it → 0.6.
        var summoned = Bands(
            ["black metal", "ambient"],
            ["black metal"],
            ["black metal", "doom"]);
        var banished = Bands(
            ["black metal"],
            ["death metal"],
            ["black metal", "punk"],
            ["black metal"],
            ["folk"]);

        MirrorMath.MirrorResult r = MirrorMath.Compute(summoned, banished);

        Assert.True(r.HasData);
        Assert.Equal("black metal", r.FavouriteTag);
        Assert.Equal(5, r.BanishedTotal);
        Assert.Equal(3, r.BanishedMatching);
        Assert.Equal(0.6, r.Fraction, 9); // invert the numerator and this 0.6 breaks
    }

    [Fact]
    public void Compute_FavouriteIsMostFrequent_TieBrokenAlphabetically()
    {
        // "a" and "b" each appear twice; the tie must break alphabetically to "a" for determinism.
        var summoned = Bands(["a", "b"], ["a"], ["b"]);

        MirrorMath.MirrorResult r = MirrorMath.Compute(summoned, Bands(["a"]));

        Assert.Equal("a", r.FavouriteTag);
    }

    [Fact]
    public void Compute_IsCaseInsensitive()
    {
        var summoned = Bands(["Black Metal"]);
        var banished = Bands(["black metal"], ["BLACK METAL"], ["doom"]);

        MirrorMath.MirrorResult r = MirrorMath.Compute(summoned, banished);

        Assert.Equal(2, r.BanishedMatching);
    }

    [Fact]
    public void Compute_NoBanished_HasNoData()
    {
        MirrorMath.MirrorResult r = MirrorMath.Compute(Bands(["black metal"]), Bands());

        Assert.False(r.HasData);
        Assert.Equal(0, r.BanishedTotal);
        Assert.Equal(0.0, r.Fraction);
    }

    [Fact]
    public void Compute_NoSummonedTags_HasNoFavourite_AndNoData()
    {
        // Nothing summoned (or summoned bands are untagged) → no favourite to reflect against.
        MirrorMath.MirrorResult r = MirrorMath.Compute(Bands(), Bands(["black metal"]));

        Assert.False(r.HasData);
        Assert.Null(r.FavouriteTag);
    }

    [Fact]
    public void Compute_RepeatedTagOnOneBand_CountsOncePerBand()
    {
        // A band that lists "doom" twice must not stuff the favourite count.
        var summoned = Bands(["doom", "doom"], ["sludge"], ["sludge"]);

        MirrorMath.MirrorResult r = MirrorMath.Compute(summoned, Bands(["sludge"]));

        Assert.Equal("sludge", r.FavouriteTag); // sludge (2 bands) beats doom (1 band)
    }
}
