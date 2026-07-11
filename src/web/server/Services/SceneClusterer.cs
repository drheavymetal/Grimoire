using Grimoire.Library.Models;

namespace Grimoire.Server.Services;

/// <summary>
/// Clusters bands into <b>scenes</b> — a city, a decade and a tag taken together (B20/C11):
/// Gothenburg in the 90s playing melodic death metal, Tampa in the late 80s playing death metal.
/// This is deliberately <b>not</b> a map of bands by country (D17): the unit is the local scene,
/// so the city and the decade are as load-bearing as the genre.
///
/// <para>
/// The clustering is a pure fold over already-filtered inputs (city known, formation year known),
/// so it is unit-tested without a database. A band with several tags belongs to several scenes —
/// that is correct, a band sits in more than one local movement — so tags are exploded, never
/// invented. Scenes smaller than <c>minSize</c> are dropped: two bands do not make a scene.
/// </para>
/// </summary>
public static class SceneClusterer
{
    /// <summary>One band's contribution to the clustering. Tags are already lower-cased upstream.</summary>
    public readonly record struct SceneInput(
        Guid Id,
        string Name,
        Rank? Rank,
        string City,
        int Decade,
        IReadOnlyList<string> Tags);

    /// <summary>A band inside a scene.</summary>
    public record SceneBand(Guid Id, string Name, Rank? Rank);

    /// <summary>A scene: a (city, decade, tag) cluster and the bands that fall in it.</summary>
    public record Scene(string City, int Decade, string Tag, IReadOnlyList<SceneBand> Bands)
    {
        public int Size => Bands.Count;
    }

    /// <summary>
    /// The decade an artist's formation year falls in (1991 → 1990). Pure and total.
    /// </summary>
    public static int DecadeOf(int year)
    {
        return (int)Math.Floor(year / 10.0) * 10;
    }

    /// <summary>
    /// Folds the inputs into scenes of at least <paramref name="minSize"/> bands, ordered by size
    /// (largest first), then by city and decade for a stable result. A band with no tags contributes
    /// to no scene (a scene needs a genre); a tag shared by too few bands is dropped, not padded.
    /// </summary>
    public static IReadOnlyList<Scene> Cluster(IReadOnlyList<SceneInput> members, int minSize)
    {
        ArgumentNullException.ThrowIfNull(members);

        if (minSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minSize), "A scene needs at least one band.");
        }

        // Bucket bands by (city, decade, tag). A band is added once per distinct tag it carries.
        Dictionary<(string City, int Decade, string Tag), List<SceneBand>> buckets = [];

        foreach (SceneInput m in members)
        {
            if (string.IsNullOrWhiteSpace(m.City))
            {
                continue;
            }

            foreach (string tag in m.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct())
            {
                var key = (m.City, m.Decade, tag);
                if (!buckets.TryGetValue(key, out List<SceneBand>? bands))
                {
                    bands = [];
                    buckets[key] = bands;
                }

                bands.Add(new SceneBand(m.Id, m.Name, m.Rank));
            }
        }

        return buckets
            .Where(b => b.Value.Count >= minSize)
            .Select(b => new Scene(
                b.Key.City,
                b.Key.Decade,
                b.Key.Tag,
                b.Value.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderByDescending(s => s.Size)
            .ThenBy(s => s.City, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Decade)
            .ThenBy(s => s.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
