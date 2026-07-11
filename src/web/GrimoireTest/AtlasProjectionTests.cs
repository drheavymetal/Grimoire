using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

public class AtlasProjectionTests
{
    // Four embeddings lying in the plane spanned by the first two coordinates, shifted off the
    // origin so the mean is non-zero (centring must actually do something). The two score columns
    // are uncorrelated with different variance, so the top two principal components are exactly the
    // first two axes and the "stored xy" is the centred (a, b) pair — the same thing the offline
    // atlas pass would produce. A constant tail is centred out.
    private static readonly float[][] Embeddings =
    [
        [4f, 3f, 5f, 5f, 5f],
        [0f, 3f, 5f, 5f, 5f],
        [2f, 4f, 5f, 5f, 5f],
        [2f, 2f, 5f, 5f, 5f],
    ];

    private static readonly double[] Xs = [2, -2, 0, 0];
    private static readonly double[] Ys = [0, 0, 1, -1];

    [Fact]
    public void Reconstruct_ReproducesTheStoredCoordinatesForEveryStar()
    {
        AtlasProjection.Basis? basis = AtlasProjection.Reconstruct(Embeddings, Xs, Ys);

        Assert.NotNull(basis);

        for (int i = 0; i < Embeddings.Length; i++)
        {
            (double x, double y) = AtlasProjection.Project(basis!, Embeddings[i]);
            Assert.Equal(Xs[i], x, 4);
            Assert.Equal(Ys[i], y, 4);
        }
    }

    [Fact]
    public void Project_PlacesAFreshVectorByItsCentredCoordinates()
    {
        AtlasProjection.Basis basis = AtlasProjection.Reconstruct(Embeddings, Xs, Ys)!;

        // Taste = [5, 7, 5, 5, 5]; centred against μ = [2, 3, 5, 5, 5] this is (3, 4) in the plane.
        // A projection that skipped the centring would land at (5, 7) instead — this bites on it.
        (double x, double y) = AtlasProjection.Project(basis, [5f, 7f, 5f, 5f, 5f]);

        Assert.Equal(3.0, x, 4);
        Assert.Equal(4.0, y, 4);
    }

    [Fact]
    public void Reconstruct_TooFewStars_ReturnsNull()
    {
        Assert.Null(AtlasProjection.Reconstruct([[1f, 2f]], [0.5], [0.5]));
    }

    [Fact]
    public void Reconstruct_LengthMismatch_ReturnsNull()
    {
        Assert.Null(AtlasProjection.Reconstruct(Embeddings, [1, 2], Ys));
    }

    [Fact]
    public void Reconstruct_DimensionMismatch_ReturnsNull()
    {
        float[][] ragged = [[1f, 2f, 3f], [1f, 2f]];
        Assert.Null(AtlasProjection.Reconstruct(ragged, [1, -1], [1, -1]));
    }

    [Fact]
    public void Reconstruct_DegenerateAxis_ReturnsNull()
    {
        // No variance in the x-scores: nothing to project the first axis onto.
        Assert.Null(AtlasProjection.Reconstruct(Embeddings, [0, 0, 0, 0], Ys));
    }

    [Fact]
    public void Project_DimensionMismatch_Throws()
    {
        AtlasProjection.Basis basis = AtlasProjection.Reconstruct(Embeddings, Xs, Ys)!;
        Assert.Throws<ArgumentException>(() => AtlasProjection.Project(basis, [1f, 2f]));
    }
}
