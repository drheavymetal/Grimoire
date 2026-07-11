using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// One hit of a semantic search (B2): an artist plus the cosine distance of its embedding from the
/// (centred) query vector — nearer is more alike.
/// </summary>
public record SemanticHitDto(
    Guid Id,
    string Name,
    string? Country,
    int? FormedYear,
    Rank? Rank,
    double Distance);
