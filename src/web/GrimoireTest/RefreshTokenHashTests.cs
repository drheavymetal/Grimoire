using Grimoire.Server.Auth;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The refresh-token hash (D28): the persisted key for a refresh token is the lower-case hex SHA-256
/// of the raw token — the token itself is never stored, so a leak of refresh_tokens cannot mint
/// sessions. These bite on determinism (the same token must hash to the same 64-hex row key across
/// register/login and every later refresh/logout lookup) and on the format the schema reserves.
/// </summary>
public class RefreshTokenHashTests
{
    [Fact]
    public void HashToken_IsDeterministic()
    {
        // The whole revocation scheme relies on this: a logout/refresh must find the SAME row the
        // login wrote, which only works if the hash is stable for a given token.
        Assert.Equal(TokenService.HashToken("a-refresh-token"), TokenService.HashToken("a-refresh-token"));
    }

    [Fact]
    public void HashToken_IsLowercaseHexOf32Bytes()
    {
        string hash = TokenService.HashToken("another-token");

        Assert.Equal(64, hash.Length); // SHA-256 = 32 bytes = 64 hex chars; the column reserves 64.
        Assert.All(hash, c => Assert.True(
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
            $"unexpected hex char '{c}'"));
    }

    [Fact]
    public void HashToken_DifferentTokens_DifferentHashes()
    {
        Assert.NotEqual(TokenService.HashToken("token-one"), TokenService.HashToken("token-two"));
    }

    [Fact]
    public void HashToken_KnownVector()
    {
        // SHA-256("") — a fixed anchor so an accidental change to the algorithm (encoding, casing)
        // is caught, not just self-consistency.
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            TokenService.HashToken(string.Empty));
    }
}
