using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The scene clustering (B20/C11): city + decade + tag, not a country map (D17). These bite — a
/// band with several tags must land in several scenes, a scene under the floor must be dropped, and
/// the decade must round down.
/// </summary>
public class SceneClustererTests
{
    private static SceneClusterer.SceneInput Band(string name, string city, int decade, params string[] tags)
    {
        return new SceneClusterer.SceneInput(Guid.NewGuid(), name, null, city, decade, tags);
    }

    [Fact]
    public void DecadeOf_RoundsDownToTheDecade()
    {
        Assert.Equal(1990, SceneClusterer.DecadeOf(1991));
        Assert.Equal(1990, SceneClusterer.DecadeOf(1999));
        Assert.Equal(1980, SceneClusterer.DecadeOf(1989));
        Assert.Equal(2000, SceneClusterer.DecadeOf(2000));
    }

    [Fact]
    public void Cluster_GroupsBandsSharingCityDecadeAndTag()
    {
        List<SceneClusterer.SceneInput> members =
        [
            Band("Entombed", "Stockholm", 1990, "death metal"),
            Band("Dismember", "Stockholm", 1990, "death metal"),
            Band("Unleashed", "Stockholm", 1990, "death metal"),
        ];

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(members, minSize: 3);

        SceneClusterer.Scene scene = Assert.Single(scenes);
        Assert.Equal("Stockholm", scene.City);
        Assert.Equal(1990, scene.Decade);
        Assert.Equal("death metal", scene.Tag);
        Assert.Equal(3, scene.Size);
        // Ordered by name.
        Assert.Equal(["Dismember", "Entombed", "Unleashed"], scene.Bands.Select(b => b.Name));
    }

    [Fact]
    public void Cluster_DropsScenesBelowTheMinimumSize()
    {
        // Two bands do not make a scene at minSize 3 (flip the floor and this would surface).
        List<SceneClusterer.SceneInput> members =
        [
            Band("A", "Bergen", 1990, "black metal"),
            Band("B", "Bergen", 1990, "black metal"),
        ];

        Assert.Empty(SceneClusterer.Cluster(members, minSize: 3));
        Assert.Single(SceneClusterer.Cluster(members, minSize: 2));
    }

    [Fact]
    public void Cluster_PutsAMultiTagBandInEveryMatchingScene()
    {
        // One band with two tags contributes to two distinct scenes.
        List<SceneClusterer.SceneInput> members =
        [
            Band("At the Gates", "Gothenburg", 1990, "melodic death metal", "death metal"),
            Band("Dark Tranquillity", "Gothenburg", 1990, "melodic death metal", "death metal"),
        ];

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(members, minSize: 2);

        Assert.Equal(2, scenes.Count);
        Assert.Contains(scenes, s => s.Tag == "melodic death metal" && s.Size == 2);
        Assert.Contains(scenes, s => s.Tag == "death metal" && s.Size == 2);
    }

    [Fact]
    public void Cluster_SeparatesDifferentDecadesAndCities()
    {
        List<SceneClusterer.SceneInput> members =
        [
            Band("Tampa89a", "Tampa", 1980, "death metal"),
            Band("Tampa89b", "Tampa", 1980, "death metal"),
            Band("Tampa90a", "Tampa", 1990, "death metal"),
            Band("Tampa90b", "Tampa", 1990, "death metal"),
        ];

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(members, minSize: 2);

        Assert.Equal(2, scenes.Count);
        Assert.All(scenes, s => Assert.Equal(2, s.Size));
        Assert.Contains(scenes, s => s.Decade == 1980);
        Assert.Contains(scenes, s => s.Decade == 1990);
    }

    [Fact]
    public void Cluster_OrdersBySizeDescending()
    {
        List<SceneClusterer.SceneInput> members =
        [
            Band("a", "CityA", 1990, "t"),
            Band("b", "CityA", 1990, "t"),
            Band("c", "CityB", 1990, "t"),
            Band("d", "CityB", 1990, "t"),
            Band("e", "CityB", 1990, "t"),
        ];

        IReadOnlyList<SceneClusterer.Scene> scenes = SceneClusterer.Cluster(members, minSize: 2);

        Assert.Equal(3, scenes[0].Size);
        Assert.Equal("CityB", scenes[0].City);
    }
}
