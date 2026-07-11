using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class TeacherStudentResolverTests
{
    private static readonly Guid Queried = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Forward_QueriedIsTeacher()
    {
        // MusicBrainz "teacher" forward: the queried artist taught the target.
        TeacherStudentPair? pair = TeacherStudentResolver.Resolve("teacher", "forward", Queried, Target);

        Assert.NotNull(pair);
        Assert.Equal(Queried, pair!.Value.TeacherMbid);
        Assert.Equal(Target, pair.Value.StudentMbid);
    }

    [Fact]
    public void Backward_QueriedIsStudent()
    {
        // Verified live: querying Beethoven, Haydn appears as "teacher" backward (Haydn taught him).
        TeacherStudentPair? pair = TeacherStudentResolver.Resolve("teacher", "backward", Queried, Target);

        Assert.NotNull(pair);
        Assert.Equal(Target, pair!.Value.TeacherMbid);
        Assert.Equal(Queried, pair.Value.StudentMbid);
    }

    [Fact]
    public void Direction_IsNotSymmetric()
    {
        // The whole point: swapping the direction swaps teacher and student. If it did not, the
        // lineage would be inverted (a student shown as the master), so this must bite.
        TeacherStudentPair forward = TeacherStudentResolver.Resolve("teacher", "forward", Queried, Target)!.Value;
        TeacherStudentPair backward = TeacherStudentResolver.Resolve("teacher", "backward", Queried, Target)!.Value;

        Assert.Equal(forward.TeacherMbid, backward.StudentMbid);
        Assert.Equal(forward.StudentMbid, backward.TeacherMbid);
        Assert.NotEqual(forward.TeacherMbid, backward.TeacherMbid);
    }

    [Theory]
    [InlineData("member of band")]
    [InlineData("composer")]
    [InlineData("")]
    [InlineData(null)]
    public void NonTeacherRelation_IsIgnored(string? type)
    {
        Assert.Null(TeacherStudentResolver.Resolve(type, "forward", Queried, Target));
    }

    [Fact]
    public void UnknownDirection_IsRefused()
    {
        // With no forward/backward we cannot tell teacher from student, so we refuse to guess.
        Assert.Null(TeacherStudentResolver.Resolve("teacher", "sideways", Queried, Target));
        Assert.Null(TeacherStudentResolver.Resolve("teacher", null, Queried, Target));
    }

    [Fact]
    public void SelfRelation_IsRefused()
    {
        Assert.Null(TeacherStudentResolver.Resolve("teacher", "forward", Queried, Queried));
    }

    [Fact]
    public void EmptyEndpoint_IsRefused()
    {
        Assert.Null(TeacherStudentResolver.Resolve("teacher", "forward", Guid.Empty, Target));
        Assert.Null(TeacherStudentResolver.Resolve("teacher", "backward", Queried, Guid.Empty));
    }
}
