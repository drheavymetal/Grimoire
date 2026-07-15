namespace Grimoire.Server.Dtos;

/// <summary>
/// The result of a LIGHT taste face-off between two friends (FRIENDS wave): a side-by-side of how
/// deep each has gone and how their grimoires and tastes line up. Async and read-only — there is no
/// realtime and no new table; either friend opens the same view for themselves.
///
/// <para>
/// <see cref="MyDepth"/>/<see cref="TheirDepth"/> are the two users' Depth Scores (feature B15) and
/// <see cref="Winner"/> is <c>"me"</c>, <c>"them"</c> or <c>"tie"</c> by that score — rarer (higher)
/// wins. <see cref="Shared"/>/<see cref="MineOnly"/>/<see cref="TheirsOnly"/> are grimoire-cross
/// counts (feature C23): bands both have summoned, bands only the caller has, bands only the friend
/// has. <see cref="Alignment"/> is the cosine similarity (0..1) of the two centred taste vectors —
/// null when either user has no taste yet.
/// </para>
///
/// <para>
/// Named <c>DuelFaceOffDto</c> rather than <c>DuelResultDto</c> because that name is already taken by
/// the blind-duel reveal (feature C2, <see cref="DuelResultDto"/> in DuelDecadeDtos). Only the JSON
/// field names cross the wire, and those match the contract exactly.
/// </para>
/// </summary>
public record DuelFaceOffDto(
    int MyDepth,
    int TheirDepth,
    string Winner,
    int Shared,
    int MineOnly,
    int TheirsOnly,
    double? Alignment);
