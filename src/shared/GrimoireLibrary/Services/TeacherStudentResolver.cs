namespace Grimoire.Library.Services;

/// <summary>
/// A teacher/student relationship resolved from a single MusicBrainz "teacher" artist-relation,
/// expressed canonically as (teacher → student) MBIDs regardless of which endpoint was queried.
/// This is the raw material of the classical lineage (movement VII, D11): MusicBrainz documents
/// teacher/student between people far better than any pedagogical source for metal.
/// </summary>
public readonly record struct TeacherStudentPair(Guid TeacherMbid, Guid StudentMbid);

/// <summary>
/// Pure logic that turns one MusicBrainz artist-relation into a <see cref="TeacherStudentPair"/>,
/// independent of the JSON DTOs and of the database, so it can be unit-tested directly.
///
/// MusicBrainz models the "teacher" relationship as directed entity0 (teacher) → entity1 (student).
/// When we query an artist, a relation with <c>direction == "forward"</c> means the queried artist
/// is the teacher and the target is the student; <c>"backward"</c> means the queried artist is the
/// student and the target is the teacher. Verified live: querying Beethoven, Haydn appears as a
/// "teacher" relation with direction "backward" (Haydn taught Beethoven).
/// </summary>
public static class TeacherStudentResolver
{
    /// <summary>The only MusicBrainz relation type that denotes a teacher/student link.</summary>
    public const string TeacherType = "teacher";

    /// <summary>
    /// Resolves one relation fetched while querying <paramref name="queriedMbid"/> into a canonical
    /// (teacher, student) pair. Returns null when the relation is not a teacher relation, when an
    /// endpoint id is empty, or when both ends are the same artist — so the caller never fabricates
    /// an edge.
    /// </summary>
    /// <param name="relationType">The MusicBrainz relation type (e.g. "teacher").</param>
    /// <param name="direction">"forward" (queried is the teacher) or "backward" (queried is the student).</param>
    /// <param name="queriedMbid">MBID of the artist whose relations were fetched.</param>
    /// <param name="targetMbid">MBID of the artist on the other end of the relation.</param>
    public static TeacherStudentPair? Resolve(
        string? relationType,
        string? direction,
        Guid queriedMbid,
        Guid targetMbid)
    {
        if (!string.Equals(relationType, TeacherType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (queriedMbid == Guid.Empty || targetMbid == Guid.Empty || queriedMbid == targetMbid)
        {
            return null;
        }

        // forward: queried taught target. backward: target taught queried.
        bool queriedIsTeacher = string.Equals(direction, "forward", StringComparison.OrdinalIgnoreCase);

        if (!queriedIsTeacher && !string.Equals(direction, "backward", StringComparison.OrdinalIgnoreCase))
        {
            // Unknown direction: we cannot tell teacher from student, so we refuse to guess.
            return null;
        }

        Guid teacher = queriedIsTeacher ? queriedMbid : targetMbid;
        Guid student = queriedIsTeacher ? targetMbid : queriedMbid;

        return new TeacherStudentPair(teacher, student);
    }
}
