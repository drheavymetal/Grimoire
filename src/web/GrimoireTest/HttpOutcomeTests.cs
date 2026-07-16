using System.Net;
using Grimoire.Library.Enrichment;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The rule every enrichment pass stamps its resume marker by. Getting it wrong is not a cosmetic
/// bug: call a permanent failure transient and the pass retries it for ever without draining; call
/// a transient failure permanent and an outage is recorded as fact about the band, invisibly and
/// with no way to tell it from a real miss afterwards (MEMORY §6f).
/// </summary>
public class HttpOutcomeTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void ServerCouldNotAnswer_IsTransient(HttpStatusCode status)
    {
        Assert.True(HttpOutcome.IsTransient(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]      // the 400 that looped the five slashed titles
    [InlineData(HttpStatusCode.Forbidden)]       // MA cutting us off: real, and not fixed by retrying
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    public void ServerAnsweredNo_IsDefinitive(HttpStatusCode status)
    {
        Assert.False(HttpOutcome.IsTransient(status));
    }

    [Fact]
    public void Success_IsNotTransient()
    {
        Assert.False(HttpOutcome.IsTransient(HttpStatusCode.OK));
    }
}
