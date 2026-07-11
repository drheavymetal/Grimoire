using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>One side of a band comparison (B24): identity and the tags that feed the overlap.</summary>
public record CompareBandDto(
    Guid Id,
    string Name,
    Rank? Rank,
    string? Country,
    IReadOnlyList<string> Tags);

/// <summary>A person who is a member of both compared bands (B24).</summary>
public record SharedMemberDto(Guid Id, string Name);

/// <summary>
/// The comparison of two bands (B24): the tags they share and how much (Jaccard), the cosine
/// distance between their sound (null when either has no embedding — never invented), and the
/// members they have in common.
/// </summary>
public record CompareResultDto(
    CompareBandDto A,
    CompareBandDto B,
    IReadOnlyList<string> SharedTags,
    double TagSimilarity,
    double? VectorDistance,
    IReadOnlyList<SharedMemberDto> SharedMembers);
