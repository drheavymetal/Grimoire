using System.Net;
using System.Text;
using Grimoire.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The just-in-time preview resolver's matching (DECISIONS D25/D19): iTunes is asked first and Deezer
/// only complements it, a band whose name does not match exactly is discarded (a missing preview beats
/// the wrong band's audio), and an exact Deezer id already stored on the artist skips the name search.
/// These bite: reversing the source order, loosening the name match, or ignoring the stored id each
/// flips an assertion. No network — a stub handler answers every call.
/// </summary>
public class PreviewResolverTests
{
    private const string ITunesPreview = "https://audio-ssl.itunes.apple.com/preview/darkthrone.m4a";
    private const string DeezerPreview = "https://cdns-preview-a.dzcdn.net/stream/darkthrone.mp3";

    [Fact]
    public async Task Resolve_PrefersITunes_WhenBothMatch()
    {
        StubHandler handler = new(request =>
        {
            if (IsITunes(request))
            {
                return Ok($"{{\"resultCount\":1,\"results\":[{{\"artistName\":\"Darkthrone\",\"previewUrl\":\"{ITunesPreview}\"}}]}}");
            }

            return Ok("{\"data\":[{\"id\":42,\"name\":\"Darkthrone\"}]}");
        });

        PreviewResolution? result = await Resolver(handler).ResolveAsync("Darkthrone", null, default);

        Assert.NotNull(result);
        Assert.Equal("iTunes", result!.Source);
        Assert.Equal(ITunesPreview, result.Url);

        // iTunes answered first, so Deezer was never called (never the reverse — D25).
        Assert.DoesNotContain(handler.Requests, r => r.Contains("api.deezer.com"));
    }

    [Fact]
    public async Task Resolve_FallsBackToDeezer_WhenITunesHasNoMatch()
    {
        StubHandler handler = new(request =>
        {
            if (IsITunes(request))
            {
                // A result, but for a different band — must be discarded, not served.
                return Ok("{\"resultCount\":1,\"results\":[{\"artistName\":\"Not The Band\",\"previewUrl\":\"https://audio-ssl.itunes.apple.com/wrong.m4a\"}]}");
            }

            if (request.RequestUri!.AbsolutePath.Contains("search/artist"))
            {
                return Ok("{\"data\":[{\"id\":42,\"name\":\"Darkthrone\"}]}");
            }

            return Ok($"{{\"data\":[{{\"preview\":\"{DeezerPreview}\"}}]}}");
        });

        PreviewResolution? result = await Resolver(handler).ResolveAsync("Darkthrone", null, default);

        Assert.NotNull(result);
        Assert.Equal("Deezer", result!.Source);
        Assert.Equal(DeezerPreview, result.Url);
    }

    [Fact]
    public async Task Resolve_ReturnsNull_WhenNeitherSourceMatchesTheBand()
    {
        StubHandler handler = new(request =>
        {
            if (IsITunes(request))
            {
                return Ok("{\"resultCount\":1,\"results\":[{\"artistName\":\"Wrong One\",\"previewUrl\":\"https://audio-ssl.itunes.apple.com/wrong.m4a\"}]}");
            }

            // Deezer search also returns only a wrong band.
            return Ok("{\"data\":[{\"id\":99,\"name\":\"Another Wrong\"}]}");
        });

        PreviewResolution? result = await Resolver(handler).ResolveAsync("Darkthrone", null, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task Resolve_UsesStoredDeezerId_WithoutASearch()
    {
        StubHandler handler = new(request =>
        {
            if (IsITunes(request))
            {
                return Ok("{\"resultCount\":0,\"results\":[]}");
            }

            // The top-track endpoint for the exact id 7434 carried in the artist's links.
            return Ok($"{{\"data\":[{{\"preview\":\"{DeezerPreview}\"}}]}}");
        });

        Dictionary<string, string> links = new()
        {
            ["free streaming"] = "https://www.deezer.com/artist/7434",
        };

        PreviewResolution? result = await Resolver(handler).ResolveAsync("Darkthrone", links, default);

        Assert.NotNull(result);
        Assert.Equal("Deezer", result!.Source);
        Assert.Equal(DeezerPreview, result.Url);

        // The exact id was used: the top endpoint was hit for 7434 and no name search was made.
        Assert.Contains(handler.Requests, r => r.Contains("artist/7434/top"));
        Assert.DoesNotContain(handler.Requests, r => r.Contains("search/artist"));
    }

    private static bool IsITunes(HttpRequestMessage request)
    {
        return request.RequestUri!.Host.Contains("itunes.apple.com");
    }

    private static PreviewResolver Resolver(HttpMessageHandler handler)
    {
        return new PreviewResolver(new StubFactory(handler), NullLogger<PreviewResolver>.Instance);
    }

    private static (HttpStatusCode, string) Ok(string json)
    {
        return (HttpStatusCode.OK, json);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> _respond;

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond)
        {
            _respond = respond;
        }

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            (HttpStatusCode status, string body) = _respond(request);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            string baseAddress = name == PreviewResolver.ITunesClientName
                ? "https://itunes.apple.com/"
                : "https://api.deezer.com/";

            return new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri(baseAddress),
            };
        }
    }
}
