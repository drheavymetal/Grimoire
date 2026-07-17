using Grimoire.Library.Models;
using Grimoire.Library.Services;

namespace Grimoire.Server.Services;

/// <summary>
/// What asking "can this band sound?" ended in. The outcome is in the contract rather than inferred
/// from a null, for the reason DECISIONS D61 had to learn the hard way: "we found nothing" and "we
/// did not look" are different facts, and collapsing them into one null is what turns a negative
/// cache into a lie. Here it also tells the caller whether the artist row was mutated and therefore
/// needs saving.
/// </summary>
public enum ProbeOutcome
{
    /// <summary>A cached, allow-listed preview URL was already on the row. No network, no mutation.</summary>
    AlreadyAudible,

    /// <summary>Resolved online just-in-time and cached on the row. Mutated.</summary>
    Resolved,

    /// <summary>Probed now and nothing streamable came back; the negative was cached. Mutated.</summary>
    Inaudible,

    /// <summary>Probed on an earlier pass and known inaudible — skipped without a network call.</summary>
    AlreadyProbed,
}

/// <summary>
/// Resolves whether a band can actually sound, just-in-time (DECISIONS D25/D19/D40), and caches both
/// the answer and its absence. Extracted from <c>RiteController</c> so The Rite and the games share
/// ONE definition of "probed" and "audible": the subtle part is the negative cache (a band that
/// resolves to nothing must not be re-resolved on every draw), and two copies of that rule would
/// drift apart silently.
///
/// Nothing here invents a <c>preview_url</c> (Invariant 5) and nothing here streams: the URL is only
/// resolved, and the bytes still go through the capability proxy (<c>PreviewAudioProxy</c>, D32),
/// which re-validates the resolved URL against its allow-list. Mutations are left unsaved on purpose
/// — the caller batches one <c>SaveChangesAsync</c> for a whole walk.
/// </summary>
public sealed class PreviewProbe
{
    private readonly PreviewResolver _previews;
    private readonly ILogger<PreviewProbe> _logger;

    public PreviewProbe(PreviewResolver previews, ILogger<PreviewProbe> logger)
    {
        _previews = previews;
        _logger = logger;
    }

    /// <summary>
    /// True once an artist has been probed for a preview, whether or not one was found: it carries at
    /// least one curated <c>listen:</c> link (the same marker the ETL's preview pass leaves). A probed
    /// band with a null <c>preview_url</c> is genuinely inaudible and is not re-resolved every draw.
    /// </summary>
    public static bool WasProbed(IReadOnlyDictionary<string, string>? links)
    {
        return links is not null
            && links.Keys.Any(k => k.StartsWith(StreamingLinks.Prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Records that an artist was probed by merging the curated search links into <c>links</c> (the
    /// ETL's convention, reused here). This is the negative-cache marker AND supplies the reveal's
    /// outbound streaming links. A new dictionary instance makes the change detectable to EF; the raw
    /// MusicBrainz url-rels already in the column are preserved.
    /// </summary>
    public static void MarkProbed(Artist artist)
    {
        ArgumentNullException.ThrowIfNull(artist);

        if (string.IsNullOrWhiteSpace(artist.Name))
        {
            return;
        }

        Dictionary<string, string> merged = artist.Links is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(artist.Links, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> link in StreamingLinks.Build(artist.Name, null, null))
        {
            merged[link.Key] = link.Value;
        }

        artist.Links = merged;
    }

    /// <summary>Whether an outcome means the band can be streamed to a listener right now.</summary>
    public static bool IsAudible(ProbeOutcome outcome)
    {
        return outcome is ProbeOutcome.AlreadyAudible or ProbeOutcome.Resolved;
    }

    /// <summary>Whether an outcome wrote to the artist row, so the caller knows a save is owed.</summary>
    public static bool Mutated(ProbeOutcome outcome)
    {
        return outcome is ProbeOutcome.Resolved or ProbeOutcome.Inaudible;
    }

    /// <summary>
    /// Makes one band audible if it can be: a cached allow-listed URL passes straight through, a band
    /// already probed and found silent is skipped without a network call, and anything else is
    /// resolved online (iTunes first, then Deezer — D25) and cached, negatives included. The
    /// <paramref name="artist"/> must be a TRACKED entity: the cache writes land on it and the caller
    /// saves them.
    /// </summary>
    public async Task<ProbeOutcome> EnsureAudibleAsync(Artist artist, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(artist);

        if (PreviewAudioProxy.IsAllowed(artist.PreviewUrl))
        {
            return ProbeOutcome.AlreadyAudible;
        }

        if (WasProbed(artist.Links))
        {
            return ProbeOutcome.AlreadyProbed;
        }

        PreviewResolution? resolution = await _previews.ResolveAsync(artist.Name, artist.Links, ct);

        if (resolution is not null && PreviewAudioProxy.IsAllowed(resolution.Url))
        {
            artist.PreviewUrl = resolution.Url;
            MarkProbed(artist);

            _logger.LogInformation("Band resolved a preview just-in-time from {Source}.", resolution.Source);

            return ProbeOutcome.Resolved;
        }

        // Nothing streamable came back: cache the negative so the next draw skips it.
        MarkProbed(artist);

        return ProbeOutcome.Inaudible;
    }
}
