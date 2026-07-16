using System.Net;

namespace Grimoire.Library.Enrichment;

/// <summary>
/// The single place that decides whether an HTTP failure means "ask again later" or "there is
/// nothing here". Every enrichment source classifies through this, so the rule is one rule
/// rather than four subtly different ones scattered across the sources (MEMORY §6f).
/// </summary>
public static class HttpOutcome
{
    /// <summary>
    /// Whether <paramref name="status"/> is the server failing to answer rather than answering
    /// "no". Transient: 408 (request timeout), 429 (rate limited) and every 5xx — the server is
    /// telling us it could not serve us <em>now</em>. Everything else, 404 and the rest of the
    /// 4xx family included, is a definitive statement about this request and safe to stamp:
    /// a 400 from a malformed title will still be a 400 tomorrow.
    /// </summary>
    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)status >= 500;
}
