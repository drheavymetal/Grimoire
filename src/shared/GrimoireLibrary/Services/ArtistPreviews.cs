using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// One clip a source offered, before it is anything the database has an opinion about: the URL, the
/// <c>IEnrichmentSource.Name</c> that returned it, and the track title if the source gave one. The
/// artist match has already been verified by the caller (<see cref="NameMatch"/>) — nothing downstream
/// re-checks it, because only the caller knows which artist it asked about.
/// </summary>
public sealed record PreviewCandidate(string Url, string Source, string? TrackTitle);

/// <summary>
/// One clip chosen to play, and whether it is genuinely a different recording from the one the
/// listener already heard. <see cref="Source"/> and <see cref="TrackTitle"/> are nullable because a
/// clip stored before this table existed is a bare URL on <c>artists.preview_url</c> with no record of
/// where it came from — saying "unknown" is the honest answer there (Invariant 5), and inventing an
/// attribution would be worse than showing none.
/// </summary>
/// <param name="Url">The preview URL to stream, through the capability proxy — never handed to a client directly (D32).</param>
/// <param name="Source">Which source stands behind the clip, or null when it is a legacy URL of unrecorded origin.</param>
/// <param name="TrackTitle">The track, or null when unknown. <b>Leaks the band</b> — never render before the answer is in.</param>
/// <param name="IsDifferentTrack">
/// False when this is the very clip already heard, served again because the band has no other. A caller
/// that cares (a guessing game measures knowledge of the band, not memory of one clip) can tell, and
/// one that does not can ignore it and still always get playable audio.
/// </param>
public sealed record PreviewChoice(string Url, string? Source, string? TrackTitle, bool IsDifferentTrack);

/// <summary>
/// The rules for an artist's alternate clips (<see cref="ArtistPreview"/>): what to keep out of what a
/// source offered, and which one to play to someone who has already heard one of them.
///
/// <para>
/// Pure, and in <c>shared</c>, for the same reason <see cref="ArtistBiographies"/> is: two producers
/// write these rows (the ETL's preview pass, and — should it ever be wired — the just-in-time resolve
/// at serve time) and any consumer reads them. A rule as quiet as "is this actually a different song?"
/// cannot be re-implemented per caller; the copies drift and nothing fails loudly when they do.
/// </para>
/// </summary>
public static class ArtistPreviews
{
    /// <summary>
    /// How many clips are kept per artist. Deliberately a handful and not the 25 iTunes hands over:
    /// alternates for a guessing game, not an index of the band's tracks. Grimoire does not play music
    /// (Invariant 4) and Apple's terms are already strained by the blind Rite (R9) — a few spare cuts
    /// does not change what the app is, and a stored discography per band would start to.
    /// </summary>
    public const int MaxPerArtist = 5;

    /// <summary>
    /// Longest URL kept. A preview URL is ~90–110 characters in practice; this is the guard that keeps
    /// the composite primary key inside PostgreSQL's btree entry limit, so a freak URL is dropped
    /// (leaving a gap) rather than failing the whole pass's INSERT.
    /// </summary>
    public const int MaxUrlLength = 512;

    /// <summary>
    /// The clips to <b>add</b> for an artist: everything in <paramref name="candidates"/> that is not
    /// already stored and not the same song under another URL, in the order offered (iTunes before
    /// Deezer — D25), stopping once the artist holds <see cref="MaxPerArtist"/>.
    ///
    /// <para>
    /// Additions only: nothing existing is rewritten or dropped. Running a pass twice over the same
    /// answers therefore adds nothing the second time, which is what makes the pass re-runnable — and
    /// re-running is not hypothetical, it is what D61 requires whenever a source failed to answer.
    /// </para>
    /// <para>
    /// The same-song rule is what makes "a different track" true rather than merely "a different row".
    /// iTunes and Deezer both know a band's best-known song and return it under two unrelated CDN URLs;
    /// keeping both would let a game play the very song it just played and announce it as new. Titles
    /// are compared normalised (<see cref="NameMatch.Normalize"/> — case, diacritics, whitespace).
    /// Remasters and live cuts survive as separate rows, and correctly so: they are different
    /// recordings, even if a listener would call them the same song.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ArtistPreview> Additions(
        Guid artistId,
        IReadOnlyList<ArtistPreview> existing,
        IEnumerable<PreviewCandidate> candidates,
        DateTime collectedAt)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidates);

        HashSet<string> urls = existing
            .Select(p => p.Url)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> titles = existing
            .Where(p => !string.IsNullOrWhiteSpace(p.TrackTitle))
            .Select(p => NameMatch.Normalize(p.TrackTitle))
            .ToHashSet(StringComparer.Ordinal);

        int room = MaxPerArtist - existing.Count;
        List<ArtistPreview> additions = [];

        foreach (PreviewCandidate candidate in candidates)
        {
            if (additions.Count >= room)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(candidate.Url)
                || candidate.Url.Length > MaxUrlLength
                || string.IsNullOrWhiteSpace(candidate.Source))
            {
                continue;
            }

            if (!urls.Add(candidate.Url))
            {
                continue;
            }

            string? title = string.IsNullOrWhiteSpace(candidate.TrackTitle) ? null : candidate.TrackTitle.Trim();

            // An untitled clip cannot be compared by song, so it is kept: a possible duplicate beats
            // discarding a real clip on a guess.
            if (title is not null && !titles.Add(NameMatch.Normalize(title)))
            {
                continue;
            }

            additions.Add(new ArtistPreview
            {
                ArtistId = artistId,
                Url = candidate.Url,
                Source = candidate.Source,
                TrackTitle = title,
                CollectedAt = collectedAt,
            });
        }

        return additions;
    }

    /// <summary>
    /// A clip to play to someone who already heard <paramref name="heardUrl"/> — a different recording
    /// when the artist has one, and otherwise the same clip again, said so.
    ///
    /// <para>
    /// Falling back rather than returning nothing is the point: an artist whose only clip is the one
    /// already heard is still playable, just less of a test. A caller that would rather skip such a band
    /// reads <see cref="PreviewChoice.IsDifferentTrack"/>; a caller that just wants audio ignores it.
    /// Null comes back only when there is no audio at all to play.
    /// </para>
    /// <para>
    /// "Different" excludes the heard URL <em>and</em> anything sharing its title, so a band whose two
    /// stored clips are the same song from two sources is honestly reported as having no alternate
    /// rather than replaying that song as new. With <paramref name="heardUrl"/> null or blank nothing
    /// has been heard, so any clip is unheard and every choice is a different track.
    /// </para>
    /// </summary>
    /// <param name="stored">The artist's stored clips. Empty is normal — most of the catalogue has never been harvested.</param>
    /// <param name="heardUrl">The clip already heard, normally <c>artists.preview_url</c> (what The Rite served). Null when nothing was.</param>
    /// <param name="selector">
    /// What decides which alternate is picked, and the reason this takes an id rather than a
    /// <c>Random</c>: the choice must be a pure function of it. Audio is served through a capability URL
    /// that re-resolves the clip on <em>every</em> request (D32), so a pick drawn from a fresh random
    /// would hand a player a different song each time they pressed replay — a round that changes under
    /// the person answering it. Pass the round's id and the round keeps the clip it was dealt, for ever,
    /// without storing it.
    /// </param>
    public static PreviewChoice? ChooseUnheard(
        IReadOnlyList<ArtistPreview> stored,
        string? heardUrl,
        Guid selector)
    {
        ArgumentNullException.ThrowIfNull(stored);

        string? heard = string.IsNullOrWhiteSpace(heardUrl) ? null : heardUrl;

        ArtistPreview? heardRow = heard is null
            ? null
            : stored.FirstOrDefault(p => string.Equals(p.Url, heard, StringComparison.Ordinal));

        string? heardTitle = string.IsNullOrWhiteSpace(heardRow?.TrackTitle)
            ? null
            : NameMatch.Normalize(heardRow!.TrackTitle);

        List<ArtistPreview> unheard = stored
            .Where(p => !string.IsNullOrWhiteSpace(p.Url))
            .Where(p => heard is null || !string.Equals(p.Url, heard, StringComparison.Ordinal))
            .Where(p => heardTitle is null
                || string.IsNullOrWhiteSpace(p.TrackTitle)
                || !string.Equals(NameMatch.Normalize(p.TrackTitle), heardTitle, StringComparison.Ordinal))
            .ToList();

        if (unheard.Count > 0)
        {
            // Ordered before indexing: the caller's rows arrive in whatever order the database returned
            // them, and an index into an unstable order is not reproducible however stable the selector.
            unheard.Sort((a, b) => string.CompareOrdinal(a.Url, b.Url));

            ArtistPreview pick = unheard[(int)(Spread(selector) % (uint)unheard.Count)];

            return new PreviewChoice(pick.Url, pick.Source, pick.TrackTitle, IsDifferentTrack: true);
        }

        if (heard is not null)
        {
            // Only the heard clip: play it again rather than drop the band, and say it is not new.
            // Source and title come from the stored row when there is one — a band harvested before
            // this table existed has neither, and null says so instead of guessing.
            return new PreviewChoice(heard, heardRow?.Source, heardRow?.TrackTitle, IsDifferentTrack: false);
        }

        return null;
    }

    /// <summary>
    /// Spreads a selector across the clip pool: FNV-1a over the id's 16 bytes.
    /// <para>
    /// Deliberately not <c>Guid.GetHashCode()</c> and not a seeded <c>Random</c>. The first is a runtime
    /// implementation detail nothing promises to keep stable; the second is stable but says so nowhere,
    /// and both would be relied on to hand a player the same clip on every replay of a round dealt weeks
    /// earlier. This is a few lines and cannot drift under us.
    /// </para>
    /// </summary>
    private static uint Spread(Guid selector)
    {
        Span<byte> bytes = stackalloc byte[16];
        selector.TryWriteBytes(bytes);

        unchecked
        {
            uint hash = 2166136261;

            foreach (byte b in bytes)
            {
                hash = (hash ^ b) * 16777619;
            }

            return hash;
        }
    }
}
