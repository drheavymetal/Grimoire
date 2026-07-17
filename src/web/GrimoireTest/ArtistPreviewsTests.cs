using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The rules for a band's alternate clips (DECISIONS D67). Both halves are places a bug would be quiet
/// rather than loud, which is what these bite on: <see cref="ArtistPreviews.Additions"/> getting
/// deduplication wrong either duplicates rows onto their own primary key or silently drops real clips,
/// and <see cref="ArtistPreviews.ChooseUnheard"/> getting "different" wrong replays the very song a
/// player just heard while announcing it as new — a game that has stopped measuring anything and does
/// not say so.
/// </summary>
public class ArtistPreviewsTests
{
    private static readonly Guid Band = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static ArtistPreview Stored(string url, string source = "iTunes", string? title = null) => new()
    {
        ArtistId = Band,
        Url = url,
        Source = source,
        TrackTitle = title,
        CollectedAt = Now,
    };

    // --- Additions: what a lookup is allowed to keep ---

    [Fact]
    public void Additions_KeepsEveryTrackOfTheSameBand()
    {
        // The whole point of D67: one iTunes response carries up to 25 tracks and we kept one.
        List<PreviewCandidate> offered =
        [
            new("https://audio.itunes/1.m4a", "iTunes", "Transilvanian Hunger"),
            new("https://audio.itunes/2.m4a", "iTunes", "Slottet i det fjerne"),
            new("https://audio.itunes/3.m4a", "iTunes", "Graven takeheimens bloder"),
        ];

        IReadOnlyList<ArtistPreview> additions = ArtistPreviews.Additions(Band, [], offered, Now);

        Assert.Equal(3, additions.Count);
        Assert.All(additions, p => Assert.Equal(Band, p.ArtistId));
        Assert.Equal("Transilvanian Hunger", additions[0].TrackTitle);
        Assert.Equal("iTunes", additions[0].Source);
        Assert.Equal(Now, additions[0].CollectedAt);
    }

    [Fact]
    public void Additions_IsIdempotent_ARerunAddsNothing()
    {
        // A pass MUST be re-runnable: D61 requires a re-run whenever a source failed to answer, and the
        // rows are keyed on (artist, url), so a second write of the same clip is not a duplicate — it is
        // an INSERT onto an existing primary key, i.e. a crashed pass.
        List<PreviewCandidate> offered =
        [
            new("https://audio.itunes/1.m4a", "iTunes", "Freezing Moon"),
            new("https://audio.itunes/2.m4a", "iTunes", "Funeral Fog"),
        ];

        List<ArtistPreview> first = [.. ArtistPreviews.Additions(Band, [], offered, Now)];
        IReadOnlyList<ArtistPreview> second = ArtistPreviews.Additions(Band, first, offered, Now);

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
    }

    [Fact]
    public void Additions_IsIdempotent_ForUntitledClipsToo()
    {
        // The test above passes even with URL deduplication removed, because the title rule happens to
        // catch the same clips — so on its own it does not prove what it appears to. Untitled clips are
        // deliberately kept (nothing can compare them by song), which leaves the URL as the ONLY thing
        // standing between a re-run and an INSERT onto an existing primary key. This is the case that
        // would actually crash the pass, and the one that has no second line of defence.
        List<PreviewCandidate> offered =
        [
            new("https://audio.itunes/1.m4a", "iTunes", null),
            new("https://audio.itunes/2.m4a", "iTunes", null),
        ];

        List<ArtistPreview> first = [.. ArtistPreviews.Additions(Band, [], offered, Now)];
        IReadOnlyList<ArtistPreview> second = ArtistPreviews.Additions(Band, first, offered, Now);

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
    }

    [Fact]
    public void Additions_SameSongFromBothSources_IsStoredOnce()
    {
        // iTunes and Deezer both know a band's best-known song and return it under two unrelated CDN
        // URLs. Keeping both would let ChooseUnheard "find an alternate" that is the same recording.
        List<PreviewCandidate> offered =
        [
            new("https://audio.itunes/1.m4a", "iTunes", "Freezing Moon"),
            new("https://cdns-preview.dzcdn.net/1.mp3", "Deezer", "freezing moon"),
        ];

        IReadOnlyList<ArtistPreview> additions = ArtistPreviews.Additions(Band, [], offered, Now);

        ArtistPreview kept = Assert.Single(additions);
        Assert.Equal("iTunes", kept.Source);
    }

    [Fact]
    public void Additions_UntitledClipsAreKept_NotGuessedAtAsDuplicates()
    {
        // Two clips with no title cannot be compared by song. Keeping them is the honest failure: a
        // possible duplicate beats discarding real audio on a guess (Invariant 5).
        List<PreviewCandidate> offered =
        [
            new("https://audio.itunes/1.m4a", "iTunes", null),
            new("https://audio.itunes/2.m4a", "iTunes", "   "),
        ];

        IReadOnlyList<ArtistPreview> additions = ArtistPreviews.Additions(Band, [], offered, Now);

        Assert.Equal(2, additions.Count);
        Assert.All(additions, p => Assert.Null(p.TrackTitle));
    }

    [Fact]
    public void Additions_StopsAtTheCap_CountingWhatIsAlreadyStored()
    {
        // The cap is per artist, not per run: alternates for a game, never a stored discography
        // (Invariant 4 — Grimoire does not play music).
        List<ArtistPreview> existing = [Stored("https://audio.itunes/0.m4a", title: "Kathaarian Life Code")];

        List<PreviewCandidate> offered = Enumerable.Range(1, 20)
            .Select(i => new PreviewCandidate($"https://audio.itunes/{i}.m4a", "iTunes", $"Track {i}"))
            .ToList();

        IReadOnlyList<ArtistPreview> additions = ArtistPreviews.Additions(Band, existing, offered, Now);

        Assert.Equal(ArtistPreviews.MaxPerArtist - 1, additions.Count);
    }

    [Fact]
    public void Additions_RespectsOfferedOrder_SoITunesSurvivesTheCap()
    {
        // D25: iTunes covers 41 %, Deezer 19 %. The job concatenates iTunes-then-Deezer, and the cap
        // must not quietly invert that by keeping whatever came last.
        List<PreviewCandidate> offered =
        [
            .. Enumerable.Range(1, 5).Select(i => new PreviewCandidate($"https://audio.itunes/{i}.m4a", "iTunes", $"A{i}")),
            .. Enumerable.Range(1, 5).Select(i => new PreviewCandidate($"https://cdns-preview.dzcdn.net/{i}.mp3", "Deezer", $"B{i}")),
        ];

        IReadOnlyList<ArtistPreview> additions = ArtistPreviews.Additions(Band, [], offered, Now);

        Assert.Equal(ArtistPreviews.MaxPerArtist, additions.Count);
        Assert.All(additions, p => Assert.Equal("iTunes", p.Source));
    }

    [Fact]
    public void Additions_DropsUnstorableUrls_RatherThanFailingThePass()
    {
        // A URL longer than the key can hold would fail the INSERT for the whole artist. Dropping it
        // leaves a gap, which is a thing this catalogue is full of and knows how to be.
        List<PreviewCandidate> offered =
        [
            new(new string('u', ArtistPreviews.MaxUrlLength + 1), "iTunes", "Too long"),
            new("   ", "iTunes", "Blank"),
            new("https://audio.itunes/ok.m4a", "  ", "No source"),
            new("https://audio.itunes/good.m4a", "iTunes", "Good"),
        ];

        IReadOnlyList<ArtistPreview> additions = ArtistPreviews.Additions(Band, [], offered, Now);

        ArtistPreview kept = Assert.Single(additions);
        Assert.Equal("https://audio.itunes/good.m4a", kept.Url);
    }

    [Fact]
    public void Additions_NoTracks_IsAnEmptyList_NotAnInvention()
    {
        // ~48 % of the underground is genuinely inaudible (D25). Empty is a legitimate answer.
        Assert.Empty(ArtistPreviews.Additions(Band, [], [], Now));
    }

    // --- ChooseUnheard: the contract the guessing game consumes ---

    [Fact]
    public void ChooseUnheard_PrefersATrackTheListenerHasNotHeard()
    {
        // The reason this whole wave exists: playing back the exact clip someone already heard when
        // they summoned the band measures memory of that clip, not knowledge of the band (D67).
        List<ArtistPreview> stored =
        [
            Stored("https://audio.itunes/heard.m4a", title: "Freezing Moon"),
            Stored("https://audio.itunes/other.m4a", title: "Funeral Fog"),
        ];

        PreviewChoice? choice = ArtistPreviews.ChooseUnheard(stored, "https://audio.itunes/heard.m4a", Guid.NewGuid());

        Assert.NotNull(choice);
        Assert.Equal("https://audio.itunes/other.m4a", choice.Url);
        Assert.Equal("Funeral Fog", choice.TrackTitle);
        Assert.True(choice.IsDifferentTrack);
    }

    [Fact]
    public void ChooseUnheard_ExcludesTheHeardClip_EvenWhenNothingHasATitle()
    {
        // Sibling of Additions_IsIdempotent_ForUntitledClipsToo, and it exists for the same reason: with
        // titles present, the same-song rule quietly does the URL rule's job, so every other test here
        // passes even with the URL exclusion deleted. Untitled clips are kept by design, and there the
        // URL is the only thing stopping the game replaying the exact cut the player already heard —
        // which is the one thing this whole wave exists to prevent (D67).
        List<ArtistPreview> stored =
        [
            Stored("https://audio.itunes/heard.m4a"),
            Stored("https://audio.itunes/other.m4a"),
        ];

        for (int i = 0; i < 200; i++)
        {
            PreviewChoice? choice = ArtistPreviews.ChooseUnheard(stored, "https://audio.itunes/heard.m4a", Guid.NewGuid());

            Assert.NotNull(choice);
            Assert.Equal("https://audio.itunes/other.m4a", choice.Url);
            Assert.True(choice.IsDifferentTrack);
        }
    }

    [Fact]
    public void ChooseUnheard_OnlyOneTrack_FallsBackToIt_AndSaysSo()
    {
        // A band with one clip is still playable; the caller is simply told the round is weaker.
        List<ArtistPreview> stored = [Stored("https://audio.itunes/only.m4a", title: "Freezing Moon")];

        PreviewChoice? choice = ArtistPreviews.ChooseUnheard(stored, "https://audio.itunes/only.m4a", Guid.NewGuid());

        Assert.NotNull(choice);
        Assert.Equal("https://audio.itunes/only.m4a", choice.Url);
        Assert.False(choice.IsDifferentTrack);
    }

    [Fact]
    public void ChooseUnheard_SameSongUnderAnotherUrl_IsNotADifferentTrack()
    {
        // Belt and braces over Additions' dedupe: rows harvested before that rule, or by another
        // producer, could still hold the same song twice. Serving it as new would be a silent lie —
        // the game would look like it worked and would be measuring nothing.
        List<ArtistPreview> stored =
        [
            Stored("https://audio.itunes/heard.m4a", "iTunes", "Freezing Moon"),
            Stored("https://cdns-preview.dzcdn.net/dupe.mp3", "Deezer", "FREEZING MOON"),
        ];

        PreviewChoice? choice = ArtistPreviews.ChooseUnheard(stored, "https://audio.itunes/heard.m4a", Guid.NewGuid());

        Assert.NotNull(choice);
        Assert.Equal("https://audio.itunes/heard.m4a", choice.Url);
        Assert.False(choice.IsDifferentTrack);
    }

    [Fact]
    public void ChooseUnheard_NoStoredClips_FallsBackToTheRitesCut()
    {
        // Every band in production got its audio just-in-time and has no rows here yet. The game must
        // still be playable over them, on the one clip the Rite cached.
        PreviewChoice? choice = ArtistPreviews.ChooseUnheard([], "https://audio.itunes/rite.m4a", Guid.NewGuid());

        Assert.NotNull(choice);
        Assert.Equal("https://audio.itunes/rite.m4a", choice.Url);
        Assert.False(choice.IsDifferentTrack);

        // Nothing recorded where a legacy preview_url came from, so nothing is claimed about it.
        Assert.Null(choice.Source);
        Assert.Null(choice.TrackTitle);
    }

    [Fact]
    public void ChooseUnheard_InaudibleBand_IsNull()
    {
        // No rows and no cached cut: the band cannot sound and the caller must skip it, not invent audio.
        Assert.Null(ArtistPreviews.ChooseUnheard([], null, Guid.NewGuid()));
        Assert.Null(ArtistPreviews.ChooseUnheard([], "   ", Guid.NewGuid()));
    }

    [Fact]
    public void ChooseUnheard_NothingHeardYet_AnyClipIsUnheard()
    {
        List<ArtistPreview> stored = [Stored("https://audio.itunes/1.m4a", title: "Freezing Moon")];

        PreviewChoice? choice = ArtistPreviews.ChooseUnheard(stored, null, Guid.NewGuid());

        Assert.NotNull(choice);
        Assert.True(choice.IsDifferentTrack);
    }

    [Fact]
    public void ChooseUnheard_DrawsFromTheWholeUnheardPool_NeverTheHeardOne()
    {
        // Different rounds must reach every alternate: a game that always serves track two is a game
        // with one alternate wearing a bigger table. And across every draw, the heard clip stays out.
        List<ArtistPreview> stored =
        [
            Stored("https://audio.itunes/heard.m4a", title: "Heard"),
            .. Enumerable.Range(1, 4).Select(i => Stored($"https://audio.itunes/{i}.m4a", title: $"Track {i}")),
        ];

        HashSet<string> seen = [];

        for (int i = 0; i < 200; i++)
        {
            PreviewChoice? choice = ArtistPreviews.ChooseUnheard(stored, "https://audio.itunes/heard.m4a", Guid.NewGuid());
            Assert.NotNull(choice);
            Assert.True(choice.IsDifferentTrack);
            seen.Add(choice.Url);
        }

        Assert.Equal(4, seen.Count);
        Assert.DoesNotContain("https://audio.itunes/heard.m4a", seen);
    }

    [Fact]
    public void ChooseUnheard_SameRound_AlwaysGetsTheSameClip()
    {
        // The property the whole selector exists for. Audio is served through a capability URL that
        // re-resolves the clip on every request (D32), so this is called again on every replay of a
        // round dealt weeks ago — from another process, after a redeploy. If the pick were drawn from a
        // fresh random, or hashed through anything the runtime is free to change, pressing replay would
        // play a DIFFERENT song and quietly hand the player a second guess.
        List<ArtistPreview> stored =
        [
            Stored("https://audio.itunes/heard.m4a", title: "Heard"),
            .. Enumerable.Range(1, 4).Select(i => Stored($"https://audio.itunes/{i}.m4a", title: $"Track {i}")),
        ];

        Guid round = new("8f14e45f-ceea-467a-9575-8ac6f6a9b1c3");

        PreviewChoice? first = ArtistPreviews.ChooseUnheard(stored, "https://audio.itunes/heard.m4a", round);

        for (int i = 0; i < 20; i++)
        {
            PreviewChoice? again = ArtistPreviews.ChooseUnheard(stored, "https://audio.itunes/heard.m4a", round);
            Assert.Equal(first!.Url, again!.Url);
        }

        // And it does not move when the rows come back in another order, which is what an unordered
        // SELECT is entitled to do between two calls.
        List<ArtistPreview> shuffled = [.. Enumerable.Reverse(stored)];

        PreviewChoice? reordered = ArtistPreviews.ChooseUnheard(shuffled, "https://audio.itunes/heard.m4a", round);

        Assert.Equal(first!.Url, reordered!.Url);
    }
}
