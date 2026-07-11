using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// A performer on a release (feature B9): who played, on which instruments, and whether they were
/// an official member or a guest/session player. <see cref="IsGuest"/> is the D9 distinction —
/// an official member and a guest are different facts, and confusing them ruins the Gantt.
/// </summary>
public record PerformerCreditDto(
    Guid ArtistId,
    string Name,
    Rank? Rank,
    IReadOnlyList<string> Instruments,
    bool IsGuest);

/// <summary>A production credit on a release (feature B9): who produced, engineered, mixed or mastered it.</summary>
public record ProductionCreditDto(
    Guid ArtistId,
    string Name,
    string Role);

/// <summary>
/// The credits of one release (feature B9), keyed by <see cref="ReleaseId"/> so the artist page can
/// match it to the discography it already holds. A release the credits ETL never reached simply
/// does not appear — the front renders a designed "no credits" state for it.
/// </summary>
public record ReleaseCreditsDto(
    Guid ReleaseId,
    IReadOnlyList<PerformerCreditDto> Performers,
    IReadOnlyList<ProductionCreditDto> Production);

/// <summary>One member who joined or left the band around the pivotal release (feature B12).</summary>
public record TurnoverMemberDto(Guid Id, string Name);

/// <summary>
/// "The disc where everything changed" (feature B12): the release with the greatest lineup turnover
/// around its date, and who came in and went out near it. Null from the endpoint when no dated
/// release sees any change — an honest empty state, never a manufactured drama.
/// </summary>
public record PivotalReleaseDto(
    Guid ReleaseId,
    string Title,
    int? Year,
    int Score,
    IReadOnlyList<TurnoverMemberDto> Joined,
    IReadOnlyList<TurnoverMemberDto> Left);
