using System.Net;
using Grimoire.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Covers the on-disk caching contract of <see cref="CoverArtCache"/> without hitting the network:
/// hits and misses are both cached (asked upstream exactly once), but transient failures are not.
/// </summary>
public sealed class CoverArtCacheTests : IDisposable
{
    private readonly string _dir;

    public CoverArtCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "grimoire-cover-test-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private CoverArtCache Build(StubHandler handler)
    {
        HttpClient http = new(handler) { BaseAddress = new Uri("https://coverartarchive.org/") };
        IOptions<CoverCacheOptions> options = Options.Create(new CoverCacheOptions { Directory = _dir });
        return new CoverArtCache(http, options, NullLogger<CoverArtCache>.Instance);
    }

    [Fact]
    public async Task Found_IsCachedToDisk_AndSecondCallDoesNotRefetch()
    {
        byte[] payload = [0x01, 0x02, 0x03, 0x04];
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        });
        CoverArtCache cache = Build(handler);
        Guid mbid = Guid.NewGuid();

        CoverResult first = await cache.GetAsync(mbid, CancellationToken.None);
        Assert.Equal(CoverOutcome.Found, first.Outcome);
        Assert.NotNull(first.FilePath);
        Assert.Equal(payload, await File.ReadAllBytesAsync(first.FilePath!));

        CoverResult second = await cache.GetAsync(mbid, CancellationToken.None);
        Assert.Equal(CoverOutcome.Found, second.Outcome);

        // Served from disk the second time: upstream was asked exactly once.
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task NotFound_IsCachedAsNegative_AndSecondCallDoesNotRefetch()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        CoverArtCache cache = Build(handler);
        Guid mbid = Guid.NewGuid();

        CoverResult first = await cache.GetAsync(mbid, CancellationToken.None);
        Assert.Equal(CoverOutcome.NotFound, first.Outcome);
        Assert.True(File.Exists(Path.Combine(_dir, mbid.ToString("D") + ".404")));

        CoverResult second = await cache.GetAsync(mbid, CancellationToken.None);
        Assert.Equal(CoverOutcome.NotFound, second.Outcome);

        // The 404 is a cached fact, not re-asked.
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TransientFailure_IsNotCached_AndSecondCallRefetches()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        CoverArtCache cache = Build(handler);
        Guid mbid = Guid.NewGuid();

        CoverResult first = await cache.GetAsync(mbid, CancellationToken.None);
        Assert.Equal(CoverOutcome.Unavailable, first.Outcome);

        // No marker of any kind is written for a transient failure.
        Assert.False(File.Exists(Path.Combine(_dir, mbid.ToString("D") + ".404")));
        Assert.False(File.Exists(Path.Combine(_dir, mbid.ToString("D") + ".jpg")));

        CoverResult second = await cache.GetAsync(mbid, CancellationToken.None);
        Assert.Equal(CoverOutcome.Unavailable, second.Outcome);

        // A 503 must be retried, not remembered: upstream is asked both times.
        Assert.Equal(2, handler.Calls);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_responder(request));
        }
    }
}
