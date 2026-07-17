using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>A band inside a scene (B20/C11): enough to name it, tint it by rank and click through.</summary>
public record SceneBandDto(Guid Id, string Name, Rank? Rank);

/// <summary>
/// A scene (B20/C11): a city, a decade and a sound family taken together, and the bands in it.
/// Deliberately not a country map (D17).
///
/// <para>
/// <paramref name="Lift"/> is why the scene is on the page at all: how many times its usual share
/// this family holds here (1.0 = as common as anywhere, 10.0 = ten times over). <paramref name="Size"/>
/// stays, but it ranks nothing — it is the evidence behind the lift, not the reason for it.
/// </para>
/// </summary>
public record SceneDto(
    string City,
    int Decade,
    string Family,
    int Size,
    double Lift,
    IReadOnlyList<SceneBandDto> Bands);
