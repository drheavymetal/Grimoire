using Grimoire.Server.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The gift capability token (C22). These bite: a sealed token must round-trip to the exact band,
/// and a tampered or foreign token must fail to open (never leak a band). Uses an ephemeral
/// protector so no key ring is needed.
/// </summary>
public class GiftTokenTests
{
    private static IDataProtector Protector()
    {
        return new EphemeralDataProtectionProvider().CreateProtector(GiftToken.Purpose);
    }

    [Fact]
    public void Wrap_ThenUnwrap_RoundTripsThePayload()
    {
        IDataProtector p = Protector();
        Guid artist = Guid.NewGuid();

        string token = GiftToken.Wrap(p, new GiftToken.Payload(artist, "for you, blind"));
        GiftToken.Payload? back = GiftToken.Unwrap(p, token);

        Assert.NotNull(back);
        Assert.Equal(artist, back!.ArtistId);
        Assert.Equal("for you, blind", back.Note);
    }

    [Fact]
    public void Token_DoesNotContainTheBandIdInClear()
    {
        IDataProtector p = Protector();
        Guid artist = Guid.NewGuid();

        string token = GiftToken.Wrap(p, new GiftToken.Payload(artist, null));

        // The whole point of the gift is blindness: the id must not be readable from the link.
        Assert.DoesNotContain(artist.ToString("D"), token);
        Assert.DoesNotContain(artist.ToString("N"), token);
    }

    [Fact]
    public void Unwrap_ReturnsNullForATamperedToken()
    {
        IDataProtector p = Protector();
        string token = GiftToken.Wrap(p, new GiftToken.Payload(Guid.NewGuid(), null));

        // Flip the tail: the MAC no longer verifies, so it must not open.
        string tampered = token[..^2] + (token[^1] == 'A' ? "BB" : "AA");

        Assert.Null(GiftToken.Unwrap(p, tampered));
    }

    [Fact]
    public void Unwrap_ReturnsNullForATokenFromAnotherProtector()
    {
        string token = GiftToken.Wrap(Protector(), new GiftToken.Payload(Guid.NewGuid(), null));

        // A token minted by a different key ring must not open here.
        Assert.Null(GiftToken.Unwrap(Protector(), token));
    }

    [Fact]
    public void Unwrap_ReturnsNullForGarbage()
    {
        Assert.Null(GiftToken.Unwrap(Protector(), "not-a-real-token"));
        Assert.Null(GiftToken.Unwrap(Protector(), ""));
    }
}
