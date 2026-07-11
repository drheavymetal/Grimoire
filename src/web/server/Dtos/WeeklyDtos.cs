using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// The Weekly Rite (feature B17): the seven blind bands for the current ISO week. The seven are
/// deterministic for the week (same week → same seven for everyone), and each item is a blind rite
/// the user can play through the audio proxy and resolve with Summon/Banish/Again like any other.
/// </summary>
public record WeeklyRiteDto(
    string WeekKey,
    IReadOnlyList<WeeklyItemDto> Items);

/// <summary>
/// One of the week's seven, served blind (SPEC §5.3): the capability token, the risk, the proxied
/// audio URL — never the band's name. <see cref="Resolved"/> is true when the user already judged
/// this one this week (its <see cref="State"/> says how); an unresolved item is still blind.
/// </summary>
public record WeeklyItemDto(
    Guid Token,
    double RiskPercentile,
    string AudioUrl,
    RiteState State,
    bool Resolved);

/// <summary>
/// The result of triggering a Weekly-Rite push (feature B17 delivery). <see cref="Sent"/> is how
/// many of the caller's subscriptions the push service accepted, <see cref="Pruned"/> how many dead
/// endpoints were removed, <see cref="Failed"/> how many failed for another reason.
/// </summary>
public record NotifyResultDto(int Sent, int Pruned, int Failed);
