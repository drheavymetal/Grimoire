using Grimoire.Library.Models;
using Grimoire.Library.Services;

namespace Grimoire.Server.Services;

/// <summary>
/// Clusters bands into <b>scenes</b> — a city, a decade and a sound family taken together (B20/C11).
/// This is deliberately <b>not</b> a map of bands by country (D17): the unit is the local scene, so
/// the city and the decade are as load-bearing as the sound.
///
/// <para>
/// A scene is ranked by <b>lift</b>, not by headcount. Headcount answers "where are there many
/// bands?", and the answer is always the biggest city wearing the vaguest tag — Los Angeles in the
/// 2000s playing "rock", London in the 60s playing "psychedelic rock". That is a census, not a
/// scene. Lift answers the question the page is actually asking: <b>is this sound over-represented
/// here, against the whole catalogue?</b> A city sounding like everywhere else scores 1, however
/// many bands it holds; a city that concentrated a sound where nobody expected it scores high,
/// however small it is.
/// </para>
///
/// <para>
/// The clustering is a pure fold over already-filtered inputs (city known, formation year known),
/// so it is unit-tested without a database. Nothing is invented: a band whose tags name none of the
/// <see cref="Families"/> does not enter the clustering at all, rather than being padded into a
/// family it never played.
/// </para>
/// </summary>
public static class SceneClusterer
{
    /// <summary>
    /// The sound families a scene can be made of, in the order a tag is tested against them.
    ///
    /// <para>
    /// These are the <see cref="RiteGenres"/> needles minus <c>progressive</c>, <c>folk</c> and
    /// <c>rock</c>: those three are so broad that they name no scene. "Rock" is what half the
    /// catalogue is wearing, so a "rock" cluster only ever rediscovers the largest city on the map.
    /// </para>
    ///
    /// <para>
    /// The order is <see cref="RiteGenres.All"/>'s own, and it decides compounds within a single
    /// tag: "symphonic black metal" is black metal because black metal is tested first, which is
    /// where a listener would file it too.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<SceneFamily> Families = RiteGenres.All
        .Where(g => g.Key is not ("progressive" or "folk" or "rock"))
        .Select(g => new SceneFamily(g.Needle, SceneLabelFor(g)))
        .ToList();

    /// <summary>A sound family as a scene names it: the tag substring, and the heading it prints.</summary>
    private readonly record struct SceneFamily(string Needle, string Label);

    /// <summary>
    /// The heading a family prints, which is <b>not</b> always <see cref="RiteGenre.Label"/>.
    ///
    /// <para>
    /// A <see cref="RiteGenres"/> label names the <em>lane a listener picks</em>, and two of its
    /// needles are deliberately broader than their label: <c>"gothic"</c> also catches gothic rock,
    /// <c>"symphonic"</c> also catches symphonic rock. For a blind lane that breadth is harmless and
    /// wanted — nothing is asserted, the band is served unnamed. A scene heading is the opposite: it
    /// is a claim printed next to the bands, and reusing those labels would make it false for about
    /// half the bands it catches (measured 2026-07-17 on production: 390 of 787 bands tagged
    /// "gothic" carry no "gothic metal" tag; 392 of 679 for "symphonic").
    /// </para>
    ///
    /// <para>
    /// The case that forced this: Nagoya's 1990s cluster is a real, famous scene and the lift
    /// ranking finds it — but its bands (Lamiel, kein, FANATIC◇CRISIS, Laputa) are <em>nagoya kei</em>
    /// visual kei tagged plainly "gothic", one of them with no metal tag at all. Heading them
    /// "Gothic Metal" would invent a genre they never played, which is the one thing the catalogue
    /// pages must never do (invariant 5, D17). "Gothic" is what the data actually says.
    /// </para>
    /// </summary>
    private static string SceneLabelFor(RiteGenre genre)
    {
        return genre.Key switch
        {
            "gothic-metal" => "Gothic",
            "symphonic-metal" => "Symphonic",
            _ => genre.Label,
        };
    }

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

    /// <summary>
    /// A scene: a (city, decade, family) cluster, the bands in it, and how over-represented that
    /// family is here against the catalogue at large (<see cref="Lift"/> — 1.0 means "exactly as
    /// common as everywhere else", 10.0 means "ten times its usual share").
    /// </summary>
    public record Scene(string City, int Decade, string Family, double Lift, IReadOnlyList<SceneBand> Bands)
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
    /// The single sound family a band's tags put it in, or <c>null</c> when its tags name none of
    /// <see cref="Families"/> — an honest "not a band we can place", never a guess.
    ///
    /// <para>
    /// The tags are walked <b>in order</b> and the first one that names a family wins, because
    /// MusicBrainz orders tags by how many people voted for them: the first tag is what the band
    /// mostly is. Scanning family-first instead would let one stray tag buried deep in the list
    /// capture the band — the same bug <see cref="SeedPool.FamilyOf"/> guards against, where a
    /// buried "funk metal" put the Red Hot Chili Peppers in a metal slot. A band belongs to one
    /// family here, not to every family it brushes against, or every big city would field a full
    /// set of scenes built out of its bands' incidental tags.
    /// </para>
    /// </summary>
    public static string? FamilyOf(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return null;
        }

        foreach (string tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            string lowered = tag.ToLowerInvariant();

            foreach (SceneFamily family in Families)
            {
                if (lowered.Contains(family.Needle, StringComparison.Ordinal))
                {
                    return family.Label;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Folds the inputs into scenes of at least <paramref name="minSize"/> bands, ordered by
    /// <see cref="Scene.Lift"/> (most concentrated first), then by size and city/decade/family for a
    /// stable result.
    ///
    /// <para>
    /// The floor is not decoration. Lift rewards concentration, and the most concentrated thing in
    /// any catalogue is a city we barely have data for: five bands out of five in one sound scores
    /// enormously and means nothing — it is a hole in the data wearing a scene's clothes. The floor
    /// is what separates "a sound took root here" from "we only know five bands here".
    /// </para>
    ///
    /// <para>
    /// Both denominators — the city-decade's population and the family's catalogue-wide share — are
    /// counted over <b>distinct bands</b> inside the classified universe. Every band carries exactly
    /// one family, so one band is one row and the two agree by construction; a band the tags cannot
    /// place is outside both the numerator and the denominators, never a silent zero.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Scene> Cluster(IReadOnlyList<SceneInput> members, int minSize)
    {
        ArgumentNullException.ThrowIfNull(members);

        if (minSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minSize), "A scene needs at least one band.");
        }

        // Classify first: one band, one family. Anything the families cannot name leaves the
        // universe entirely — it must not sit in a denominator it can never contribute to.
        List<(SceneInput Band, string Family)> classified = [];

        foreach (SceneInput m in members)
        {
            if (string.IsNullOrWhiteSpace(m.City))
            {
                continue;
            }

            string? family = FamilyOf(m.Tags);
            if (family is null)
            {
                continue;
            }

            classified.Add((m, family));
        }

        if (classified.Count == 0)
        {
            return [];
        }

        // The catalogue-wide share of each family, and the population of each city-decade: the two
        // baselines lift is measured against.
        Dictionary<string, int> corpusByFamily = [];
        Dictionary<(string City, int Decade), int> populationByCityDecade = [];

        foreach ((SceneInput band, string family) in classified)
        {
            corpusByFamily[family] = corpusByFamily.GetValueOrDefault(family) + 1;

            var place = (band.City, band.Decade);
            populationByCityDecade[place] = populationByCityDecade.GetValueOrDefault(place) + 1;
        }

        Dictionary<(string City, int Decade, string Family), List<SceneBand>> buckets = [];

        foreach ((SceneInput band, string family) in classified)
        {
            var key = (band.City, band.Decade, family);
            if (!buckets.TryGetValue(key, out List<SceneBand>? bands))
            {
                bands = [];
                buckets[key] = bands;
            }

            bands.Add(new SceneBand(band.Id, band.Name, band.Rank));
        }

        double corpusTotal = classified.Count;

        return buckets
            .Where(b => b.Value.Count >= minSize)
            .Select(b =>
            {
                double localShare = b.Value.Count / (double)populationByCityDecade[(b.Key.City, b.Key.Decade)];
                double globalShare = corpusByFamily[b.Key.Family] / corpusTotal;

                return new Scene(
                    b.Key.City,
                    b.Key.Decade,
                    b.Key.Family,
                    localShare / globalShare,
                    b.Value.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList());
            })
            .OrderByDescending(s => s.Lift)
            .ThenByDescending(s => s.Size)
            .ThenBy(s => s.City, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Decade)
            .ThenBy(s => s.Family, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
