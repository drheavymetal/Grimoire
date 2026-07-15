namespace Grimoire.Library.Services;

/// <summary>
/// One optional genre lane for The Rite. The user may narrow a blind tasting to a genre family
/// (feature added 2026-07-15, supersedes D43's flat "no genre filters" — see DECISIONS): the
/// default rite stays fully blind and random, but a listener who wants to spend an evening inside
/// viking metal can. It is still blind — no name, cover or country until they like it — so the ocean
/// is narrowed, not the ear pre-empted.
/// <para>
/// <see cref="Needle"/> is a single lower-case substring matched against the band's tags with ILIKE,
/// so it catches the compounds a family really wears — "black metal" also matches "atmospheric black
/// metal" and "melodic black metal", "folk" matches "folk metal". Coverage grows as the Last.fm pass
/// fills tags into the 87% of the pool that had none.
/// </para>
/// </summary>
public sealed record RiteGenre(string Key, string Label, string Needle);

/// <summary>
/// The catalogue of genre lanes offered in The Rite (main and weekly). One source of truth: the API
/// exposes it and resolves a key to its needle; the front renders the labels. Labels are the genres'
/// universal English names (metal subgenres are not translated), so no i18n key is needed.
/// </summary>
public static class RiteGenres
{
    public static readonly IReadOnlyList<RiteGenre> All =
    [
        new("black-metal", "Black Metal", "black metal"),
        new("death-metal", "Death Metal", "death metal"),
        new("doom-metal", "Doom Metal", "doom"),
        new("thrash-metal", "Thrash Metal", "thrash"),
        new("heavy-metal", "Heavy Metal", "heavy metal"),
        new("power-metal", "Power Metal", "power metal"),
        new("speed-metal", "Speed Metal", "speed metal"),
        new("sludge", "Sludge", "sludge"),
        new("grindcore", "Grindcore", "grind"),
        new("viking-metal", "Viking Metal", "viking"),
        new("folk-metal", "Folk Metal", "folk metal"),
        new("symphonic-metal", "Symphonic Metal", "symphonic"),
        new("gothic-metal", "Gothic Metal", "gothic"),
        new("progressive", "Progressive", "progressive"),
        new("stoner", "Stoner", "stoner"),
        new("metalcore", "Metalcore", "metalcore"),
        new("folk", "Folk", "folk"),
        new("punk", "Punk", "punk"),
        new("hardcore", "Hardcore", "hardcore"),
        new("rock", "Rock", "rock"),
    ];

    /// <summary>
    /// The tag substring for a genre key, or <c>null</c> when the key is unknown or blank — an
    /// unknown genre simply falls back to the unfiltered, fully-blind rite, never an error.
    /// </summary>
    public static string? NeedleFor(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (RiteGenre genre in All)
        {
            if (string.Equals(genre.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return genre.Needle;
            }
        }

        return null;
    }
}
