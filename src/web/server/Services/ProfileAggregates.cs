using Grimoire.Library.Models;
using Grimoire.Server.Dtos;

namespace Grimoire.Server.Services;

/// <summary>
/// One summoned band, reduced to the fields the profile portrait needs. The controller projects the
/// user's summoned rites (joined to artists) into this shape once, then <see cref="ProfileAggregates"/>
/// derives every breakdown from it in memory — a grimoire is small per user, so a single query plus
/// pure aggregation is both efficient and trivially unit-testable.
/// </summary>
public sealed record SummonedBand(
    Guid Id,
    string Name,
    Rank? Rank,
    string? Country,
    ArtistKind Kind,
    int? FormedYear,
    int? Listeners,
    IReadOnlyList<string> Tags);

/// <summary>
/// Pure aggregation of a user's summoned bands into the profile page's breakdowns (feature: user
/// profile). No database, no invention — an empty grimoire yields empty lists and a null deepest cut.
/// Unit tested on the ordering rules, which are the only non-obvious logic here.
/// </summary>
public static class ProfileAggregates
{
    /// <summary>
    /// The deepest cut: the rarest summoned band. Rarity is the <see cref="Rank"/> tier — Nameless is
    /// rarest, and a null rank (listeners unknown) is treated as the LEAST rare (unproven, never
    /// invented into rarity). Ties within a tier break toward the FEWER listeners; a null listener
    /// count sorts last within its tier. Null when the grimoire is empty.
    /// </summary>
    public static SummonedBand? DeepestCut(IReadOnlyList<SummonedBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        SummonedBand? best = null;

        foreach (SummonedBand band in bands)
        {
            if (best is null || IsRarer(band, best))
            {
                best = band;
            }
        }

        return best;
    }

    /// <summary>True when <paramref name="a"/> is rarer than <paramref name="b"/> (see <see cref="DeepestCut"/>).</summary>
    public static bool IsRarer(SummonedBand a, SummonedBand b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        int magA = RarityMagnitude(a.Rank);
        int magB = RarityMagnitude(b.Rank);

        if (magA != magB)
        {
            return magA > magB;
        }

        // Same tier: fewer listeners is rarer; a null listener count sorts last (treated as most).
        long listenersA = a.Listeners ?? long.MaxValue;
        long listenersB = b.Listeners ?? long.MaxValue;

        return listenersA < listenersB;
    }

    /// <summary>Rank as a rarity magnitude: Nameless highest, null = -1 (unknown, least rare).</summary>
    public static int RarityMagnitude(Rank? rank)
    {
        return rank is null ? -1 : (int)rank;
    }

    /// <summary>
    /// Bands per rarity tier, ordered from the common to the rare (Known → Nameless), with the
    /// unranked bucket (null rank) last. Only tiers actually present appear.
    /// </summary>
    public static IReadOnlyList<RankCountDto> RankBreakdown(IReadOnlyList<SummonedBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        return bands
            .GroupBy(b => b.Rank)
            .Select(g => new RankCountDto(g.Key, g.Count()))
            .OrderBy(r => r.Rank is null ? int.MaxValue : (int)r.Rank.Value)
            .ToList();
    }

    /// <summary>
    /// Bands per formed decade, chronological (ascending by decade). Bands with no formed year are
    /// skipped — a decade is never guessed.
    /// </summary>
    public static IReadOnlyList<DecadeCountDto> ByDecade(IReadOnlyList<SummonedBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        return bands
            .Where(b => b.FormedYear is not null)
            .GroupBy(b => DecadeScore.DecadeOf(b.FormedYear!.Value))
            .Select(g => new DecadeCountDto(g.Key, g.Count()))
            .OrderBy(d => d.Decade)
            .ToList();
    }

    /// <summary>
    /// The top <paramref name="top"/> countries by band count, count descending then country name
    /// ascending. Bands with no country are skipped — a country is never invented.
    /// </summary>
    public static IReadOnlyList<CountryCountDto> ByCountry(IReadOnlyList<SummonedBand> bands, int top)
    {
        ArgumentNullException.ThrowIfNull(bands);

        return bands
            .Where(b => !string.IsNullOrWhiteSpace(b.Country))
            .GroupBy(b => b.Country!)
            .Select(g => new CountryCountDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Country, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// The top <paramref name="top"/> genre tags by band count, count descending then tag ascending.
    /// Tags are flattened across every summoned band; blank tags are ignored.
    /// </summary>
    public static IReadOnlyList<GenreCountDto> ByGenre(IReadOnlyList<SummonedBand> bands, int top)
    {
        ArgumentNullException.ThrowIfNull(bands);

        return bands
            .SelectMany(b => b.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GenreCountDto(g.Key, g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Tag, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();
    }
}
