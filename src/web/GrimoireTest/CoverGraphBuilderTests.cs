using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The version graph shaping (C10). The one rule that matters is the cross-artist filter: the
/// <c>cover_versions</c> family is dominated by an artist's own remixes/remasters, so the "someone
/// else covered this" graph must drop every edge whose two ends share an artist.
/// </summary>
public class CoverGraphBuilderTests
{
    private static readonly Guid Satyricon = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Enslaved = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Motorhead = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void CrossArtist_KeepsEdgesBetweenDifferentArtists()
    {
        CoverGraphBuilder.RawCover[] rows =
        [
            new(Satyricon, Enslaved, "edit", "Hal Valr"),
        ];

        var kept = CoverGraphBuilder.CrossArtist(rows);

        Assert.Single(kept);
        Assert.Equal(Satyricon, kept[0].OriginalArtistId);
        Assert.Equal(Enslaved, kept[0].CoverArtistId);
    }

    [Fact]
    public void CrossArtist_DropsAnArtistsOwnVersions()
    {
        // Motörhead remixing its own recording: a real relation, but not a cross-artist cover.
        CoverGraphBuilder.RawCover[] rows =
        [
            new(Motorhead, Motorhead, "remix", "Iron Horse (2023 mix)"),
        ];

        Assert.Empty(CoverGraphBuilder.CrossArtist(rows));
    }

    [Fact]
    public void CrossArtist_KeepsOnlyTheCrossArtistSubset()
    {
        CoverGraphBuilder.RawCover[] rows =
        [
            new(Satyricon, Enslaved, "edit", "Hal Valr"),           // cross → keep
            new(Motorhead, Motorhead, "remix", "Iron Horse"),        // own → drop
            new(Enslaved, Satyricon, "instrumental", "Some Track"),  // cross → keep
        ];

        var kept = CoverGraphBuilder.CrossArtist(rows);

        Assert.Equal(2, kept.Count);
        Assert.DoesNotContain(kept, r => r.OriginalArtistId == r.CoverArtistId);
    }
}
