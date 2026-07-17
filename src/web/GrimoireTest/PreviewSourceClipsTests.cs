using System.Net;
using System.Text;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Worker.Preview;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// What the ETL's two preview sources now keep out of the answers they were already getting
/// (DECISIONS D67): every clip of the right band instead of the first one, at no extra request.
///
/// <para>
/// The rule these bite hardest on is the one that must NOT have changed. The match stays exactly as
/// conservative as it was (D25/D22 — <see cref="NameMatch"/>): more clips may never mean looser
/// matching, because a homonym's audio served as a blind discovery is D46, the worst bug this project
/// has had. Widening the net to catch more tracks would look like an improvement and be that bug.
/// </para>
/// <para>
/// No network — a stub handler answers every call. These are slower than the rest of the suite by the
/// sources' own deliberate pacing (iTunes 3 s, Deezer 1 s per call), which is real behaviour and not
/// worth mocking away.
/// </para>
/// </summary>
public class PreviewSourceClipsTests
{
    private static Artist Band() => new() { Id = Guid.NewGuid(), Name = "Darkthrone" };

    // --- iTunes: the 25 results we used to throw 24 of away ---

    [Fact]
    public async Task ITunes_KeepsEveryTrackOfTheBand_AndStillPromotesTheFirstToPreviewUrl()
    {
        StubHandler handler = new(_ => Ok("""
        {"resultCount":3,"results":[
          {"artistName":"Darkthrone","trackName":"Transilvanian Hunger","previewUrl":"https://audio-ssl.itunes.apple.com/1.m4a","artistViewUrl":"https://music.apple.com/artist/1"},
          {"artistName":"Darkthrone","trackName":"Slottet i det fjerne","previewUrl":"https://audio-ssl.itunes.apple.com/2.m4a"},
          {"artistName":"Darkthrone","trackName":"Graven takeheimens bloder","previewUrl":"https://audio-ssl.itunes.apple.com/3.m4a"}
        ]}
        """));

        EnrichmentResult result = await ITunes(handler).FetchAsync(Band(), default);

        Assert.Equal(EnrichmentOutcome.Matched, result.Outcome);
        Assert.NotNull(result.Enrichment);

        // The Rite's cut is unchanged: still the first match, still on preview_url.
        Assert.Equal("https://audio-ssl.itunes.apple.com/1.m4a", result.Enrichment!.PreviewUrl);

        Assert.Equal(3, result.Enrichment.Previews.Count);
        Assert.Equal("Transilvanian Hunger", result.Enrichment.Previews[0].TrackTitle);
        Assert.All(result.Enrichment.Previews, p => Assert.Equal("iTunes", p.Source));

        // One request. The alternates were always in that response (D67).
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ITunes_RejectsTheHomonym_EvenWhenItWouldMeanMoreClips()
    {
        // The wrong "Death" and "Toto" poisoned the spikes (D22/D25), and D46 served a session
        // drummer's audio as a blind discovery. A band's clips are its own or they are nobody's.
        StubHandler handler = new(_ => Ok("""
        {"resultCount":3,"results":[
          {"artistName":"Darkthrone Tribute Band","trackName":"Cover","previewUrl":"https://audio-ssl.itunes.apple.com/wrong1.m4a"},
          {"artistName":"Darkthrone","trackName":"Kathaarian Life Code","previewUrl":"https://audio-ssl.itunes.apple.com/right.m4a"},
          {"artistName":"The Darkthrone","trackName":"Not Them","previewUrl":"https://audio-ssl.itunes.apple.com/wrong2.m4a"}
        ]}
        """));

        EnrichmentResult result = await ITunes(handler).FetchAsync(Band(), default);

        Assert.Equal(EnrichmentOutcome.Matched, result.Outcome);

        PreviewCandidate kept = Assert.Single(result.Enrichment!.Previews);
        Assert.Equal("https://audio-ssl.itunes.apple.com/right.m4a", kept.Url);
        Assert.Equal("https://audio-ssl.itunes.apple.com/right.m4a", result.Enrichment.PreviewUrl);
    }

    [Fact]
    public async Task ITunes_BandWithNoTracks_IsNoData_WithNoClips()
    {
        // iTunes answered and has nothing: a real gap, safe to stamp (D61). ~48 % of the underground
        // is genuinely inaudible (D25) and that is not a failure.
        StubHandler handler = new(_ => Ok("""{"resultCount":0,"results":[]}"""));

        EnrichmentResult result = await ITunes(handler).FetchAsync(Band(), default);

        Assert.Equal(EnrichmentOutcome.NoData, result.Outcome);
        Assert.Null(result.Enrichment);
    }

    [Fact]
    public async Task ITunes_Unavailable_YieldsNoClipsAndNoVerdict()
    {
        // A 429 says nothing about the band. Clips must not be recorded and the caller must not stamp.
        StubHandler handler = new(_ => (HttpStatusCode.TooManyRequests, "{}"));

        EnrichmentResult result = await ITunes(handler).FetchAsync(Band(), default);

        Assert.Equal(EnrichmentOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Enrichment);
    }

    // --- Deezer: limit=1 was the stingiest form of the same waste ---

    [Fact]
    public async Task Deezer_AsksForFiveTopTracks_AndKeepsThePlayableOnes()
    {
        StubHandler handler = new(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("search/artist"))
            {
                return Ok("""{"data":[{"id":42,"name":"Darkthrone","link":"https://www.deezer.com/artist/42"}]}""");
            }

            return Ok("""
            {"data":[
              {"title":"Freezing Moon","preview":"https://cdns-preview-a.dzcdn.net/1.mp3"},
              {"title":"Silent Track","preview":""},
              {"title":"Funeral Fog","preview":"https://cdns-preview-a.dzcdn.net/2.mp3"}
            ]}
            """);
        });

        EnrichmentResult result = await Deezer(handler).FetchAsync(Band(), default);

        Assert.Equal(EnrichmentOutcome.Matched, result.Outcome);
        Assert.Equal("https://cdns-preview-a.dzcdn.net/1.mp3", result.Enrichment!.PreviewUrl);

        // The silent one is not a clip; the other two are.
        Assert.Equal(2, result.Enrichment.Previews.Count);
        Assert.All(result.Enrichment.Previews, p => Assert.Equal("Deezer", p.Source));
        Assert.Equal("Funeral Fog", result.Enrichment.Previews[1].TrackTitle);

        // The number that changed, and the only thing that had to: still two calls, not three.
        Assert.Contains(handler.Requests, r => r.Contains("artist/42/top?limit=5"));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Deezer_RejectsTheHomonym_BeforeAskingForAnyAudio()
    {
        StubHandler handler = new(_ => Ok("""{"data":[{"id":99,"name":"Dark Throne Cover Band","link":"https://www.deezer.com/artist/99"}]}"""));

        EnrichmentResult result = await Deezer(handler).FetchAsync(Band(), default);

        Assert.Equal(EnrichmentOutcome.NoData, result.Outcome);

        // No audio was even requested for the wrong band.
        Assert.DoesNotContain(handler.Requests, r => r.Contains("/top"));
    }

    // --- Plumbing ---

    private static ITunesEnrichmentSource ITunes(HttpMessageHandler handler)
    {
        return new ITunesEnrichmentSource(
            Client(handler, "https://itunes.apple.com/"), enabled: true, NullLogger<ITunesEnrichmentSource>.Instance);
    }

    private static DeezerEnrichmentSource Deezer(HttpMessageHandler handler)
    {
        return new DeezerEnrichmentSource(
            Client(handler, "https://api.deezer.com/"), enabled: true, NullLogger<DeezerEnrichmentSource>.Instance);
    }

    private static HttpClient Client(HttpMessageHandler handler, string baseAddress)
    {
        return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
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
}
