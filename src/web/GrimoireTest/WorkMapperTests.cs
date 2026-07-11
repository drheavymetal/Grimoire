using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class WorkMapperTests
{
    private static readonly Guid Composer = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string WorkMbid = "06e853f1-450a-3eb4-9272-73f5a30d0d41";

    [Fact]
    public void ValidWork_MapsWithComposerAndKind()
    {
        Work? work = WorkMapper.Map(WorkMbid, "Symphony No. 9", "Symphony", Composer);

        Assert.NotNull(work);
        Assert.Equal(Guid.Parse(WorkMbid), work!.Mbid);
        Assert.Equal("Symphony No. 9", work.Title);
        Assert.Equal("Symphony", work.Kind);
        Assert.Equal(Composer, work.ComposerId);
        Assert.NotEqual(Guid.Empty, work.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingWorkType_LeavesKindNull_NotInvented(string? type)
    {
        Work? work = WorkMapper.Map(WorkMbid, "Untitled", type, Composer);

        Assert.NotNull(work);
        Assert.Null(work!.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void BadMbid_IsRefused(string? mbid)
    {
        Assert.Null(WorkMapper.Map(mbid, "A Title", "Song", Composer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitle_IsRefused(string? title)
    {
        Assert.Null(WorkMapper.Map(WorkMbid, title, "Song", Composer));
    }

    [Fact]
    public void TitleAndKind_AreTrimmed()
    {
        Work? work = WorkMapper.Map(WorkMbid, "  Nocturne  ", "  Song  ", Composer);

        Assert.NotNull(work);
        Assert.Equal("Nocturne", work!.Title);
        Assert.Equal("Song", work.Kind);
    }
}
