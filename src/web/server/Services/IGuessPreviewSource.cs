using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>
/// One clip chosen for a guess-the-band round, and whether it is honestly a different recording from
/// the one The Rite already played to this listener when they summoned the band.
/// </summary>
/// <param name="Url">
/// The preview URL to stream, and only ever through the capability proxy (D32) — it never reaches a
/// client, because an iTunes preview URL routinely carries the band's name in its path.
/// </param>
/// <param name="IsDifferentTrack">
/// False when this is the very clip already heard, served again because the band has no other. The
/// round is still playable and still honest; it is just a weaker test, and the caller can say so,
/// count it, or log it rather than discovering it by ear.
/// </param>
public sealed record GuessClip(string Url, bool IsDifferentTrack);

/// <summary>
/// Where a guess-the-band round gets its audio: "play me this band, ideally NOT the cut this listener
/// already heard".
///
/// <para>
/// <b>Why this interface exists, and why it is this small.</b> The whole game turns on one property of
/// the data. <c>artists.preview_url</c> is a single clip per band, and it is the exact clip The Rite
/// served when the player summoned it — so a round built on it measures whether they remember 45
/// seconds of audio, not whether they know the band. That is a different, much worse game. The
/// alternate clips are being harvested in parallel (D67, a child table plus its rules); this contract
/// is the seam between the two, so that neither side had to wait for the other and the splice is one
/// implementation, not a rewrite.
/// </para>
/// <para>
/// <b>Until the alternates land, <see cref="RiteClipSource"/> honestly returns the Rite's own cut with
/// <see cref="GuessClip.IsDifferentTrack"/> false.</b> The game works, degraded and truthfully so —
/// which is the house rule for a missing source (Invariant 5), and better than a feature that cannot
/// be seen until two waves finish at once.
/// </para>
/// </summary>
public interface IGuessPreviewSource
{
    /// <summary>
    /// Chooses a clip to play for one round, preferring a recording other than
    /// <paramref name="heardUrl"/>. Returns null when the band has no audio at all — an ordinary
    /// answer for roughly half the underground (D25), and the caller's cue to leave it out of the deal
    /// rather than to deal a silent round.
    /// </summary>
    /// <param name="artist">
    /// The band. Must be a TRACKED entity: an implementation may resolve a preview just-in-time (D40)
    /// and cache it on the row, and the caller owns the <c>SaveChangesAsync</c> for the whole walk.
    /// </param>
    /// <param name="heardUrl">
    /// The clip this listener already heard — <c>artists.preview_url</c>, what The Rite served them.
    /// Null when they have heard nothing, in which case any clip is unheard.
    /// </param>
    /// <param name="selector">
    /// What makes the choice reproducible: the round's id. An implementation that picks among several
    /// clips MUST derive the pick from this and nothing else. The audio endpoint is a capability URL
    /// that resolves on every request, so a pick that varied per call would hand a player a different
    /// song each time they pressed replay — which is not the round they were dealt.
    /// </param>
    Task<GuessClip?> ChooseAsync(Artist artist, string? heardUrl, Guid selector, CancellationToken ct);
}

/// <summary>
/// The clip source: an alternate recording when the harvest (D67) has collected one for this band,
/// and otherwise The Rite's own cut, resolved just-in-time exactly as a serve does (D40) and reported
/// for what it is — the same track the player already heard.
///
/// <para>
/// It resolves audio online, honours the negative cache, and returns a playable, allow-listed URL. The
/// alternates are additive: a band harvested before that table existed still plays, still honestly,
/// just as a weaker test. That degradation is the house rule for a missing source (Invariant 5), not a
/// stub.
/// </para>
/// </summary>
public sealed class RiteClipSource : IGuessPreviewSource
{
    private readonly PreviewProbe _probe;
    private readonly GrimoireDbContext _db;

    public RiteClipSource(PreviewProbe probe, GrimoireDbContext db)
    {
        _probe = probe;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<GuessClip?> ChooseAsync(Artist artist, string? heardUrl, Guid selector, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(artist);

        ProbeOutcome outcome = await _probe.EnsureAudibleAsync(artist, ct);

        if (!PreviewProbe.IsAudible(outcome) || string.IsNullOrEmpty(artist.PreviewUrl))
        {
            return null;
        }

        // The alternates are a lazy navigation on a tracked entity, so they have to be asked for. An
        // unloaded collection is empty, not absent — and an empty one would silently mean "this band
        // has only the heard cut", which is the answer for a band that was never harvested and a lie
        // for one that was.
        await _db.Entry(artist).Collection(a => a.Previews).LoadAsync(ct);

        PreviewChoice? choice = ArtistPreviews.ChooseUnheard(artist.Previews, artist.PreviewUrl, selector);

        // Fall back to the Rite's cut rather than drop the band: ChooseUnheard only returns null when
        // there is no audio at all, which EnsureAudibleAsync has already ruled out.
        if (choice is null)
        {
            return new GuessClip(artist.PreviewUrl, !string.Equals(artist.PreviewUrl, heardUrl, StringComparison.Ordinal));
        }

        // The stored rows are not allow-listed by construction — `shared` cannot reference the proxy,
        // so nothing upstream could enforce D32's host allowlist as the rows were written. The proxy
        // revalidates before streaming, so an unlisted URL is never fetched; but it would be dealt as a
        // round that silently refuses to play. Check here and fall back to the cut The Rite has already
        // streamed, which is allow-listed by the fact it was served.
        if (!PreviewAudioProxy.IsAllowed(choice.Url))
        {
            return new GuessClip(artist.PreviewUrl, !string.Equals(artist.PreviewUrl, heardUrl, StringComparison.Ordinal));
        }

        // IsDifferentTrack comes from ChooseUnheard, which compares against the row it was told was
        // heard — artists.preview_url. The caller may have heard something else entirely (a gifted
        // rite, a re-resolved URL), so re-state it against the URL this listener actually heard.
        bool different = choice.IsDifferentTrack && !string.Equals(choice.Url, heardUrl, StringComparison.Ordinal);

        return new GuessClip(choice.Url, different);
    }
}
