using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The SSRF guard on the audio proxy (SPEC §5.3). The proxy only ever fetches a preview URL our
/// own ETL stored, and even then only on an allowed host. These bite: an arbitrary host or a
/// plain-http URL must be refused, or the proxy becomes a request-forwarding hole.
/// </summary>
public class PreviewAudioProxyTests
{
    [Theory]
    [InlineData("https://audio-ssl.itunes.apple.com/preview/1.m4a")]
    [InlineData("https://cdns-preview-a.dzcdn.net/stream/abc.mp3")]
    [InlineData("https://a1.mzstatic.com/preview/2.m4a")] // subdomain of an allowed apex
    public void IsAllowed_AcceptsHttpsPreviewsOnKnownHosts(string url)
    {
        Assert.True(PreviewAudioProxy.IsAllowed(url));
    }

    [Theory]
    [InlineData("http://audio-ssl.itunes.apple.com/preview/1.m4a")]   // not https
    [InlineData("https://evil.example.com/preview.mp3")]              // arbitrary host
    [InlineData("https://itunes.apple.com.evil.com/preview.mp3")]     // look-alike host
    [InlineData("ftp://cdns-preview-a.dzcdn.net/x.mp3")]              // wrong scheme
    [InlineData("/relative/path.mp3")]                                // not absolute
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void IsAllowed_RejectsEverythingElse(string? url)
    {
        Assert.False(PreviewAudioProxy.IsAllowed(url));
    }
}
