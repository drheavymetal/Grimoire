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
}
