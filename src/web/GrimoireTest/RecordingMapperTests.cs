using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class RecordingMapperTests
{
    private static readonly Guid Release = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string RecMbid = "b1a9c0e9-d987-4042-ae91-78d6a3267d69";
    private const string RecMbid2 = "c2b0d1fa-ea98-4153-bfa2-89e7b4378e7a";

    [Fact]
    public void ValidTrack_MapsTitleLengthAndPosition()
    {
        Recording? rec = RecordingMapper.Map(RecMbid, Release, "Raining Blood", "Raining Blood", 222000, 222000, 3);

        Assert.NotNull(rec);
        Assert.Equal(Guid.Parse(RecMbid), rec!.Mbid);
        Assert.Equal(Release, rec.ReleaseId);
        Assert.Equal("Raining Blood", rec.Title);
        Assert.Equal(222000, rec.LengthMs);
        Assert.Equal(3, rec.Position);
        Assert.NotEqual(Guid.Empty, rec.Id);
    }

    [Fact]
    public void TrackName_WinsOverRecordingName()
    {
        Recording? rec = RecordingMapper.Map(RecMbid, Release, "Live Intro", "Intro", 60000, 55000, 1);

        Assert.NotNull(rec);
        Assert.Equal("Live Intro", rec!.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTrackName_FallsBackToRecordingName(string? trackName)
    {
        Recording? rec = RecordingMapper.Map(RecMbid, Release, trackName, "Recording Title", 1000, 1000, 1);

        Assert.NotNull(rec);
        Assert.Equal("Recording Title", rec!.Title);
    }

    [Fact]
    public void TrackLength_WinsOverRecordingLength()
    {
        Recording? rec = RecordingMapper.Map(RecMbid, Release, "T", "T", 180000, 175000, 1);

        Assert.NotNull(rec);
        Assert.Equal(180000, rec!.LengthMs);
    }

    [Fact]
    public void MissingTrackLength_FallsBackToRecordingLength()
    {
        Recording? rec = RecordingMapper.Map(RecMbid, Release, "T", "T", null, 175000, 1);

        Assert.NotNull(rec);
        Assert.Equal(175000, rec!.LengthMs);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(-1, null)]
    public void NoOrNonPositiveLength_StaysNull_NotInvented(int? trackLen, int? recLen)
    {
        Recording? rec = RecordingMapper.Map(RecMbid, Release, "T", "T", trackLen, recLen, 1);

        Assert.NotNull(rec);
        Assert.Null(rec!.LengthMs);
    }

    [Fact]
    public void Title_IsTrimmed()
    {
        Recording? rec = RecordingMapper.Map(RecMbid, Release, "  Angel of Death  ", null, 290000, 290000, 4);

        Assert.NotNull(rec);
        Assert.Equal("Angel of Death", rec!.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void BadRecordingMbid_IsRefused(string? mbid)
    {
        Assert.Null(RecordingMapper.Map(mbid, Release, "Title", "Title", 1000, 1000, 1));
    }

    [Fact]
    public void NoTitleAtAll_IsRefused()
    {
        Assert.Null(RecordingMapper.Map(RecMbid, Release, null, null, 1000, 1000, 1));
        Assert.Null(RecordingMapper.Map(RecMbid, Release, "  ", "", 1000, 1000, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositivePosition_IsRefused(int position)
    {
        Assert.Null(RecordingMapper.Map(RecMbid, Release, "Title", "Title", 1000, 1000, position));
    }

    [Fact]
    public void CoverRelation_MapsWhenBothEndpointsParseAndDiffer()
    {
        string? relation = RecordingMapper.MapCoverRelation(RecMbid, RecMbid2, "  other versions  ");

        Assert.Equal("other versions", relation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void CoverRelation_RefusesBadEndpoint(string? badMbid)
    {
        Assert.Null(RecordingMapper.MapCoverRelation(badMbid, RecMbid2, "remix"));
        Assert.Null(RecordingMapper.MapCoverRelation(RecMbid, badMbid, "remix"));
    }

    [Fact]
    public void CoverRelation_RefusesSelfEdge()
    {
        Assert.Null(RecordingMapper.MapCoverRelation(RecMbid, RecMbid, "remix"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CoverRelation_RefusesBlankRelation(string? relation)
    {
        Assert.Null(RecordingMapper.MapCoverRelation(RecMbid, RecMbid2, relation));
    }
}
