using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class LastFmListenersTests
{
    private static readonly Guid Mbid = Guid.Parse("d1730c04-2b3f-4c2f-8c9c-0000deadbeef");
    private static readonly Guid OtherMbid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static LastFmArtistInfoResponse Info(string? name, string? mbid, string? listeners)
    {
        return new LastFmArtistInfoResponse
        {
            Artist = new LastFmArtist
            {
                Name = name,
                Mbid = mbid,
                Stats = new LastFmStats { Listeners = listeners },
            },
        };
    }

    private static LastFmArtistInfoResponse WithTags(params string?[] tagNames)
    {
        return new LastFmArtistInfoResponse
        {
            Artist = new LastFmArtist
            {
                Name = "Any Band",
                Stats = new LastFmStats { Listeners = "1000" },
                Tags = new LastFmTagList
                {
                    Tag = [.. tagNames.Select(name => new LastFmTag { Name = name })],
                },
            },
        };
    }

    // --- ParseListeners: the by-mbid path, where identity is guaranteed by the query ---

    [Fact]
    public void ParseListeners_ValidCount_ReturnsIt()
    {
        // By-mbid lookups need no name check: Last.fm returned the entity we asked for.
        LastFmArtistInfoResponse response = Info("Whatever Alias", "any-mbid", "12922");

        Assert.Equal(12922, LastFmListeners.ParseListeners(response));
    }

    [Fact]
    public void ParseListeners_ErrorSix_ReturnsNull()
    {
        // Last.fm does not index this mbid: honest null, not a borrowed same-name count.
        LastFmArtistInfoResponse response = new() { Error = 6 };

        Assert.Null(LastFmListeners.ParseListeners(response));
    }

    [Fact]
    public void ParseListeners_NullResponse_ReturnsNull()
    {
        Assert.Null(LastFmListeners.ParseListeners(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void ParseListeners_MissingOrUnparseable_ReturnsNull(string? raw)
    {
        LastFmArtistInfoResponse response = Info("Anything", "any-mbid", raw);

        Assert.Null(LastFmListeners.ParseListeners(response));
    }

    // --- Resolve: the name-path fallback for id-less artists ---

    [Fact]
    public void Resolve_MatchingNameAndMbid_ReturnsListeners()
    {
        LastFmArtistInfoResponse response = Info("Darkthrone", Mbid.ToString(), "412345");

        Assert.Equal(412345, LastFmListeners.Resolve(response, "Darkthrone", Mbid));
    }

    [Fact]
    public void Resolve_NameOnlyMatch_WhenReturnedMbidEmpty_ReturnsListeners()
    {
        LastFmArtistInfoResponse response = Info("Darkthrone", "", "999");

        Assert.Equal(999, LastFmListeners.Resolve(response, "Darkthrone", Mbid));
    }

    [Fact]
    public void Resolve_DiacriticInsensitiveNameMatch_ReturnsListeners()
    {
        // SKÁLD queried as "skald" — the same tolerant matching as previews (D25).
        LastFmArtistInfoResponse response = Info("SKÁLD", null, "50000");

        Assert.Equal(50000, LastFmListeners.Resolve(response, "skald", Guid.Empty));
    }

    [Fact]
    public void Resolve_WrongBandSameQuery_ReturnsNull()
    {
        // The classic false positive: "Toto" came back for a metal query (D22/D25).
        LastFmArtistInfoResponse response = Info("Toto", null, "2000000");

        Assert.Null(LastFmListeners.Resolve(response, "Total Death", Guid.Empty));
    }

    [Fact]
    public void Resolve_ContradictingMbid_ReturnsNull()
    {
        // Name collides but the MusicBrainz id disagrees: wrong entity.
        LastFmArtistInfoResponse response = Info("Death", OtherMbid.ToString(), "2457000");

        Assert.Null(LastFmListeners.Resolve(response, "Death", Mbid));
    }

    [Fact]
    public void Resolve_ErrorResponse_ReturnsNull()
    {
        LastFmArtistInfoResponse response = new() { Error = 6 };

        Assert.Null(LastFmListeners.Resolve(response, "Nonexistent Band", Guid.Empty));
    }

    [Fact]
    public void Resolve_NullResponse_ReturnsNull()
    {
        Assert.Null(LastFmListeners.Resolve(null, "Anything", Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void Resolve_MissingOrUnparseableListeners_ReturnsNull(string? raw)
    {
        LastFmArtistInfoResponse response = Info("Darkthrone", Mbid.ToString(), raw);

        Assert.Null(LastFmListeners.Resolve(response, "Darkthrone", Mbid));
    }

    [Fact]
    public void Resolve_ZeroListeners_IsAValidCount()
    {
        // Zero is a real, if unusual, value — distinct from "unknown" (null).
        LastFmArtistInfoResponse response = Info("Darkthrone", Mbid.ToString(), "0");

        Assert.Equal(0, LastFmListeners.Resolve(response, "Darkthrone", Mbid));
    }

    // --- ResolveByName: the mbid-then-name fallback (D41) ---

    [Fact]
    public void ResolveByName_NameMatches_AcceptsEvenWhenMbidDiffers()
    {
        // The whole point of the fallback: Last.fm indexes the band under a different mbid, which is
        // why the id lookup missed it. Name matches → accept the count. (Invert: reject on differing
        // mbid — this fails, proving the fallback works.)
        LastFmArtistInfoResponse response = Info("Iron Maiden", OtherMbid.ToString(), "3243768");

        Assert.Equal(3243768, LastFmListeners.ResolveByName(response, "Iron Maiden"));
    }

    [Fact]
    public void ResolveByName_NameMismatch_ReturnsNull()
    {
        // A same-name query that resolves to a different band must not lend its listeners (D25).
        LastFmArtistInfoResponse response = Info("Toto", null, "1200000");

        Assert.Null(LastFmListeners.ResolveByName(response, "Death"));
    }

    [Fact]
    public void ResolveByName_ErrorOrNull_ReturnsNull()
    {
        Assert.Null(LastFmListeners.ResolveByName(null, "Darkthrone"));
        Assert.Null(LastFmListeners.ResolveByName(new LastFmArtistInfoResponse { Error = 6 }, "Darkthrone"));
    }

    // --- ParseTags: genre tags ride along in the same getInfo body (MEMORY §6b) ---

    [Fact]
    public void ParseTags_ReturnsNames_InOrder_TopFive()
    {
        LastFmArtistInfoResponse response = WithTags(
            "black metal", "atmospheric black metal", "norwegian", "ambient", "lo-fi", "sixth tag");

        string[] tags = LastFmListeners.ParseTags(response);

        Assert.Equal(
            ["black metal", "atmospheric black metal", "norwegian", "ambient", "lo-fi"],
            tags);
    }

    [Fact]
    public void ParseTags_DropsJunkFolksonomy()
    {
        // "seen live" / "favorites" carry no genre signal and would pull unrelated bands together.
        LastFmArtistInfoResponse response = WithTags("seen live", "death metal", "favorites", "Favourites");

        Assert.Equal(["death metal"], LastFmListeners.ParseTags(response));
    }

    [Fact]
    public void ParseTags_TrimsBlanksAndDeduplicatesCaseInsensitively()
    {
        LastFmArtistInfoResponse response = WithTags("  Doom Metal ", null, "", "doom metal", "DOOM METAL");

        Assert.Equal(["Doom Metal"], LastFmListeners.ParseTags(response));
    }

    [Fact]
    public void ParseTags_ErrorOrMissing_ReturnsEmpty()
    {
        Assert.Empty(LastFmListeners.ParseTags(null));
        Assert.Empty(LastFmListeners.ParseTags(new LastFmArtistInfoResponse { Error = 6 }));
        Assert.Empty(LastFmListeners.ParseTags(Info("Band", "mbid", "5")));  // no tags object at all
    }
}
