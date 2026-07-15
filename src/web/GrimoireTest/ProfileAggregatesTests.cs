using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The profile portrait aggregates (user profile feature): deepest cut and the breakdowns. These
/// bite on the ordering rules — rarest wins, ties break toward fewer listeners, null rank is the
/// LEAST rare (never invented into rarity), and empty input yields empty output, never fabrication.
/// </summary>
public class ProfileAggregatesTests
{
    private static SummonedBand Band(
        string name,
        Rank? rank = null,
        int? listeners = null,
        string? country = null,
        int? formedYear = null,
        params string[] tags)
    {
        return new SummonedBand(Guid.NewGuid(), name, rank, country, ArtistKind.Group, formedYear, listeners, tags);
    }

    [Fact]
    public void DeepestCut_Empty_IsNull()
    {
        Assert.Null(ProfileAggregates.DeepestCut([]));
    }

    [Fact]
    public void DeepestCut_PicksRarestTier()
    {
        // Nameless is rarer than Known and Hidden — the whole point of inverse popularity.
        List<SummonedBand> bands =
        [
            Band("common", Rank.Known, listeners: 100_000),
            Band("rarest", Rank.Nameless, listeners: 50),
            Band("middle", Rank.Hidden, listeners: 5_000),
        ];

        SummonedBand? cut = ProfileAggregates.DeepestCut(bands);

        Assert.Equal("rarest", cut!.Name);
    }

    [Fact]
    public void DeepestCut_SameTier_BreaksToFewerListeners()
    {
        List<SummonedBand> bands =
        [
            Band("busier", Rank.Hidden, listeners: 9_000),
            Band("quieter", Rank.Hidden, listeners: 400),
        ];

        SummonedBand? cut = ProfileAggregates.DeepestCut(bands);

        Assert.Equal("quieter", cut!.Name);
    }

    [Fact]
    public void DeepestCut_NullRank_IsLeastRare_NeverWins()
    {
        // A null rank means listeners unknown — unproven rarity, so it must NOT beat a real tier.
        List<SummonedBand> bands =
        [
            Band("unknown", rank: null, listeners: null),
            Band("known", Rank.Known, listeners: 500_000),
        ];

        SummonedBand? cut = ProfileAggregates.DeepestCut(bands);

        Assert.Equal("known", cut!.Name);
    }

    [Fact]
    public void DeepestCut_AllNullRank_FallsBackToFewerListeners()
    {
        List<SummonedBand> bands =
        [
            Band("loud", rank: null, listeners: 8_000),
            Band("quiet", rank: null, listeners: 12),
        ];

        SummonedBand? cut = ProfileAggregates.DeepestCut(bands);

        Assert.Equal("quiet", cut!.Name);
    }

    [Fact]
    public void RankBreakdown_OrdersCommonToRare_NullLast()
    {
        List<SummonedBand> bands =
        [
            Band("a", Rank.Nameless),
            Band("b", Rank.Known),
            Band("c", rank: null),
            Band("d", Rank.Known),
        ];

        List<RankCountDto> breakdown = [.. ProfileAggregates.RankBreakdown(bands)];

        Assert.Equal(3, breakdown.Count);
        Assert.Equal(Rank.Known, breakdown[0].Rank);
        Assert.Equal(2, breakdown[0].Count);
        Assert.Equal(Rank.Nameless, breakdown[1].Rank);
        Assert.Null(breakdown[2].Rank);
    }

    [Fact]
    public void ByDecade_SkipsNullYear_AndSortsChronologically()
    {
        List<SummonedBand> bands =
        [
            Band("a", formedYear: 1994),
            Band("b", formedYear: 1987),
            Band("c", formedYear: 1991),
            Band("d", formedYear: null),
        ];

        List<DecadeCountDto> byDecade = [.. ProfileAggregates.ByDecade(bands)];

        Assert.Equal(2, byDecade.Count);
        Assert.Equal(1980, byDecade[0].Decade);
        Assert.Equal(1, byDecade[0].Count);
        Assert.Equal(1990, byDecade[1].Decade);
        Assert.Equal(2, byDecade[1].Count);
    }

    [Fact]
    public void ByCountry_SkipsBlank_TopByCountDescending()
    {
        List<SummonedBand> bands =
        [
            Band("a", country: "NO"),
            Band("b", country: "NO"),
            Band("c", country: "SE"),
            Band("d", country: null),
        ];

        List<CountryCountDto> byCountry = [.. ProfileAggregates.ByCountry(bands, 12)];

        Assert.Equal(2, byCountry.Count);
        Assert.Equal("NO", byCountry[0].Country);
        Assert.Equal(2, byCountry[0].Count);
        Assert.Equal("SE", byCountry[1].Country);
    }

    [Fact]
    public void ByCountry_HonoursTopN()
    {
        List<SummonedBand> bands =
        [
            Band("a", country: "NO"),
            Band("b", country: "SE"),
            Band("c", country: "FI"),
        ];

        Assert.Single(ProfileAggregates.ByCountry(bands, 1));
    }

    [Fact]
    public void ByGenre_FlattensTags_TopByCountDescending()
    {
        List<SummonedBand> bands =
        [
            Band("a", tags: ["black metal", "ambient"]),
            Band("b", tags: ["black metal"]),
            Band("c", tags: ["death metal"]),
        ];

        List<GenreCountDto> byGenre = [.. ProfileAggregates.ByGenre(bands, 12)];

        Assert.Equal("black metal", byGenre[0].Tag);
        Assert.Equal(2, byGenre[0].Count);
        Assert.Equal(3, byGenre.Count);
    }

    [Fact]
    public void ByGenre_HonoursTopN()
    {
        List<SummonedBand> bands =
        [
            Band("a", tags: ["black metal"]),
            Band("b", tags: ["death metal"]),
            Band("c", tags: ["doom"]),
        ];

        Assert.Equal(2, ProfileAggregates.ByGenre(bands, 2).Count);
    }
}
