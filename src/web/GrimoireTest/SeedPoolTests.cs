using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class SeedPoolTests
{
    [Theory]
    // The compounds land where a listener would put them, not where the substring falls first.
    [InlineData(SeedFamily.Metal, "heavy metal", "hard rock")]
    [InlineData(SeedFamily.Metal, "folk metal")]
    [InlineData(SeedFamily.Metal, "industrial metal")]
    [InlineData(SeedFamily.Metal, "nwobhm")]
    [InlineData(SeedFamily.Punk, "punk rock")]
    [InlineData(SeedFamily.Punk, "hardcore")]
    [InlineData(SeedFamily.Folk, "folk rock", "folk")]
    [InlineData(SeedFamily.Electronic, "ambient", "drone")]
    [InlineData(SeedFamily.Rock, "rock", "blues rock")]
    [InlineData(SeedFamily.Rock, "grunge")]
    public void FamilyOf_PlacesTheBandWhereAListenerWould(SeedFamily expected, params string[] tags)
    {
        Assert.Equal(expected, SeedPool.FamilyOf(tags));
    }

    [Theory]
    [InlineData("hip hop", "pop rap")]
    [InlineData("pop")]
    // The Bee Gees: a stray "baroque pop" tag names no family, so they stay Other, not miscast.
    [InlineData("disco", "pop", "baroque pop")]
    public void FamilyOf_WhatTheCatalogueSweptIn_IsOther(params string[] tags)
    {
        Assert.Equal(SeedFamily.Other, SeedPool.FamilyOf(tags));
    }

    // The bug this whole ordering exists to prevent: MusicBrainz orders tags by votes, so the first
    // tag is what the band mostly is. Scanning for a family across all tags instead lets one buried
    // tag capture the band — and the metal lane of the cold-start grid fills with rock bands that
    // happen to carry a stray metal tag, which is precisely what a metal listener does not want.
    [Theory]
    [InlineData(SeedFamily.Rock, "funk rock", "alternative rock", "rock", "funk metal")]
    [InlineData(SeedFamily.Rock, "hard rock", "rock", "heavy metal")]
    [InlineData(SeedFamily.Rock, "indie rock", "alternative rock", "post-punk revival")]
    public void FamilyOf_ATagBuriedDeep_DoesNotCaptureTheBand(SeedFamily expected, params string[] tags)
    {
        Assert.Equal(expected, SeedPool.FamilyOf(tags));
    }

    [Fact]
    public void FamilyOf_NoTags_IsOther_NeverAGuess()
    {
        Assert.Equal(SeedFamily.Other, SeedPool.FamilyOf(null));
        Assert.Equal(SeedFamily.Other, SeedPool.FamilyOf([]));
        Assert.Equal(SeedFamily.Other, SeedPool.FamilyOf(["   "]));
    }

    [Fact]
    public void Interleave_TakesOneFromEachLaneInTurn_SoNoFamilyIsBuried()
    {
        IReadOnlyList<IReadOnlyList<string>> lanes =
        [
            ["metal1", "metal2", "metal3"],
            ["rock1", "rock2", "rock3"],
            ["folk1", "folk2", "folk3"],
        ];

        List<string> merged = SeedPool.Interleave(lanes, 6, x => x);

        Assert.Equal(["metal1", "rock1", "folk1", "metal2", "rock2", "folk2"], merged);
    }

    [Fact]
    public void Interleave_ALaneThatRunsDry_DoesNotCostTheOthersTheirTurn()
    {
        IReadOnlyList<IReadOnlyList<string>> lanes =
        [
            ["metal1", "metal2", "metal3"],
            ["punk1"],
        ];

        List<string> merged = SeedPool.Interleave(lanes, 4, x => x);

        Assert.Equal(["metal1", "punk1", "metal2", "metal3"], merged);
    }

    [Fact]
    public void Interleave_ABandReachableFromTwoLanes_AppearsOnce()
    {
        IReadOnlyList<IReadOnlyList<string>> lanes =
        [
            ["Iron Maiden", "Venom"],
            ["Iron Maiden", "Saxon"],
        ];

        List<string> merged = SeedPool.Interleave(lanes, 10, x => x);

        Assert.Equal(["Iron Maiden", "Venom", "Saxon"], merged);
    }

    [Fact]
    public void Interleave_StopsAtTake_AndHandlesTheEmptyCase()
    {
        IReadOnlyList<IReadOnlyList<string>> lanes = [["a", "b", "c"]];

        Assert.Equal(["a", "b"], SeedPool.Interleave(lanes, 2, x => x));
        Assert.Empty(SeedPool.Interleave(lanes, 0, x => x));
        Assert.Empty(SeedPool.Interleave([], 5, (string x) => x));
    }
}
