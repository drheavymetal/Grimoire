using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>A band inside a scene (B20/C11): enough to name it, tint it by rank and click through.</summary>
public record SceneBandDto(Guid Id, string Name, Rank? Rank);

/// <summary>
/// A scene (B20/C11): a city, a decade and a tag taken together, and the bands that fall in it —
/// Gothenburg / 1990s / melodic death metal. Deliberately not a country map (D17).
/// </summary>
public record SceneDto(
    string City,
    int Decade,
    string Tag,
    int Size,
    IReadOnlyList<SceneBandDto> Bands);
