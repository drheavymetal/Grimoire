using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The scene clustering (B20/C11): city + decade + sound family, ranked by lift, not by headcount,
/// and not a country map (D17). These bite — the megacity wearing a generic tag must lose to the
/// small city that concentrated a sound, a band belongs to exactly one family (the first its tags
/// name), and the three broad families that name no scene must never form one.
/// </summary>
public class SceneClustererTests
{
    private static SceneClusterer.SceneInput Band(string name, string city, int decade, params string[] tags)
    {
        return new SceneClusterer.SceneInput(Guid.NewGuid(), name, null, city, decade, tags);
    }

    /// <summary>N bands in one city/decade/tag, named so they never collide across calls.</summary>
    private static IEnumerable<SceneClusterer.SceneInput> Bands(int count, string city, int decade, params string[] tags)
    {
        return Enumerable.Range(0, count).Select(i => Band($"{city}-{decade}-{tags[0]}-{i}", city, decade, tags));
    }

    [Fact]
    public void DecadeOf_RoundsDownToTheDecade()
    {
        Assert.Equal(1990, SceneClusterer.DecadeOf(1991));
        Assert.Equal(1990, SceneClusterer.DecadeOf(1999));
        Assert.Equal(1980, SceneClusterer.DecadeOf(1989));
        Assert.Equal(2000, SceneClusterer.DecadeOf(2000));
    }

    [Theory]
    [InlineData("Black Metal", "black metal")]
    [InlineData("Death Metal", "melodic death metal")]
    [InlineData("Stoner", "stoner rock")]
    [InlineData("Hardcore", "hardcore")]
    // The compound lands where a listener would file it: the family listed first inside one tag wins.
    [InlineData("Black Metal", "symphonic black metal")]
    public void FamilyOf_PlacesTheBandWhereAListenerWould(string expected, string tag)
    {
        Assert.Equal(expected, SceneClusterer.FamilyOf([tag]));
    }

    [Theory]
    // "rock", "folk" and "progressive" name half the catalogue, so they name no scene: dropped on
    // purpose. Ranking by headcount with these in play only ever rediscovered the largest city.
    [InlineData("rock")]
    [InlineData("classic rock")]
    [InlineData("folk")]
    [InlineData("progressive rock")]
    // Nothing recognised is an honest null, never a guess.
    [InlineData("hip hop")]
    [InlineData("   ")]
    public void FamilyOf_WhatNamesNoScene_IsNull(string tag)
    {
        Assert.Null(SceneClusterer.FamilyOf([tag]));
    }

    [Fact]
    public void FamilyOf_ATagBuriedDeep_DoesNotCaptureTheBand()
    {
        // MusicBrainz orders tags by votes, so the first tag is what the band mostly is. A funk band
        // carrying a buried "funk metal" is not a metal band, and must not field a metal scene.
        Assert.Null(SceneClusterer.FamilyOf(["funk", "funk rock", "funk metal"]));

        // ...but the same needle, voted first, does place it.
        Assert.Equal("Death Metal", SceneClusterer.FamilyOf(["death metal", "rock"]));
    }

    [Fact]
    public void FamilyOf_NoTags_IsNull_NeverAGuess()
    {
        Assert.Null(SceneClusterer.FamilyOf(null));
        Assert.Null(SceneClusterer.FamilyOf([]));
    }

    [Theory]
    // A scene heading is a CLAIM printed beside the bands, so it may not inherit a RiteGenres label
    // whose needle is broader than its name. "gothic" catches gothic rock and "symphonic" catches
    // symphonic rock — fine for a blind rite lane, false as a heading. Measured on production
    // (2026-07-17): about half the bands each needle catches carry no "... metal" tag at all.
    //
    // The band that forced it: Nagoya's 1990s scene is real and the ranking finds it, but its bands
    // are visual kei tagged plainly "gothic" — heading them "Gothic Metal" invents a genre.
    [InlineData("Gothic", "gothic")]
    [InlineData("Gothic", "gothic rock")]
    [InlineData("Gothic", "gothic metal")]
    [InlineData("Symphonic", "symphonic rock")]
    [InlineData("Symphonic", "symphonic metal")]
    public void FamilyOf_ALooseNeedle_NeverClaimsMetalItCannotSee(string expected, string tag)
    {
        Assert.Equal(expected, SceneClusterer.FamilyOf([tag]));
    }

    [Fact]
    public void FamilyOf_TheNagoyaBands_AreNotHeadedMetal()
    {
        // Real tag rows, copied from production. FANATIC◇CRISIS carries no metal tag whatsoever.
        Assert.Equal("Gothic", SceneClusterer.FamilyOf(["gothic", "kote kei", "metal", "nagoya kei"]));
        Assert.Equal("Gothic", SceneClusterer.FamilyOf(["nagoya kei", "rock", "gothic", "j-rock"]));
        Assert.Equal("Gothic", SceneClusterer.FamilyOf(["visual kei", "digital", "gothic", "j-rock"]));
    }

    [Fact]
    public void Cluster_GroupsBandsSharingCityDecadeAndFamily()
    {
        // One family, one city, one decade: the whole universe is this scene, so its lift is 1 —
        // as common here as everywhere, because here IS everywhere.
        List<SceneClusterer.SceneInput> members = [.. Bands(6, "Stockholm", 1990, "death metal")];

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(members, minSize: 6);

        SceneClusterer.Scene scene = Assert.Single(scenes);
        Assert.Equal("Stockholm", scene.City);
        Assert.Equal(1990, scene.Decade);
        Assert.Equal("Death Metal", scene.Family);
        Assert.Equal(6, scene.Size);
        Assert.Equal(1.0, scene.Lift, precision: 6);
        Assert.Equal(scene.Bands.Select(b => b.Name).Order(StringComparer.OrdinalIgnoreCase), scene.Bands.Select(b => b.Name));
    }

    [Fact]
    public void Cluster_DropsScenesBelowTheMinimumSize()
    {
        // Five bands do not make a scene at the default floor of 6 — that is scarcity wearing a
        // scene's clothes. Drop the floor and it surfaces.
        List<SceneClusterer.SceneInput> members = [.. Bands(5, "Bergen", 1990, "black metal")];

        Assert.Empty(SceneClusterer.Cluster(members, minSize: 6));
        Assert.Single(SceneClusterer.Cluster(members, minSize: 4));
    }

    [Fact]
    public void Cluster_PutsABandInOneFamilyOnly_NotEveryTagItWears()
    {
        // Six bands, each carrying three tags. Under the old tag-exploding fold this was three
        // scenes; a band plays one sound, so it is one.
        List<SceneClusterer.SceneInput> members =
            [.. Bands(6, "Gothenburg", 1990, "melodic death metal", "death metal", "thrash metal")];

        SceneClusterer.Scene scene = Assert.Single(SceneClusterer.Cluster(members, minSize: 6));
        Assert.Equal("Death Metal", scene.Family);
        Assert.Equal(6, scene.Size);
    }

    [Fact]
    public void Cluster_SeparatesDifferentDecadesAndCities()
    {
        List<SceneClusterer.SceneInput> members =
        [
            .. Bands(6, "Tampa", 1980, "death metal"),
            .. Bands(6, "Tampa", 1990, "death metal"),
            .. Bands(6, "Stockholm", 1990, "death metal"),
        ];

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(members, minSize: 6);

        Assert.Equal(3, scenes.Count);
        Assert.All(scenes, s => Assert.Equal(6, s.Size));
        Assert.Contains(scenes, s => s.City == "Tampa" && s.Decade == 1980);
        Assert.Contains(scenes, s => s.City == "Tampa" && s.Decade == 1990);
        Assert.Contains(scenes, s => s.City == "Stockholm" && s.Decade == 1990);
    }

    [Fact]
    public void Cluster_RanksByLift_SoTheMegacityLosesToTheConcentratedSmallOne()
    {
        // THE regression this ranking exists for. The megacity has 4x the bands of the small city,
        // but it sounds exactly like the catalogue at large. The small city is all one sound — a
        // sound that is rare everywhere else. Headcount put the megacity first; lift must not.
        List<SceneClusterer.SceneInput> members =
        [
            .. Bands(30, "Megacity", 2000, "death metal"),
            .. Bands(24, "Megacity", 2000, "black metal"),
            .. Bands(8, "Palm Desert", 1990, "stoner rock"),
        ];

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(members, minSize: 6);

        Assert.Equal("Palm Desert", scenes[0].City);
        Assert.Equal("Stoner", scenes[0].Family);
        Assert.Equal(8, scenes[0].Size);

        // Stoner is 8/62 of the catalogue but 8/8 of Palm Desert: 62/8 = 7.75x its usual share.
        Assert.Equal(62.0 / 8.0, scenes[0].Lift, precision: 6);

        // The megacity's scenes are bigger and still lose. Both score 62/54 = 1.15: the only thing
        // separating the megacity from the catalogue at large is the stoner it does not have.
        Assert.All(scenes.Where(s => s.City == "Megacity"), s => Assert.Equal(62.0 / 54.0, s.Lift, precision: 6));
        Assert.True(scenes[0].Size < scenes[1].Size);
    }

    [Fact]
    public void Cluster_BandsNamingNoFamily_LeaveTheUniverseEntirely()
    {
        // The generic bands must not sit in a denominator they can never contribute to: if "rock"
        // bands counted as population, they would dilute the local share of every real scene and
        // quietly deflate its lift.
        List<SceneClusterer.SceneInput> withGenerics =
        [
            .. Bands(6, "Bergen", 1990, "black metal"),
            .. Bands(50, "Bergen", 1990, "rock"),
        ];

        SceneClusterer.Scene scene = Assert.Single(SceneClusterer.Cluster(withGenerics, minSize: 6));
        Assert.Equal("Black Metal", scene.Family);
        Assert.Equal(6, scene.Size);
        Assert.Equal(1.0, scene.Lift, precision: 6);
    }

    [Fact]
    public void Cluster_RejectsAFloorBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneClusterer.Cluster([], minSize: 0));
    }
}
