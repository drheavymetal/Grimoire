using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The seam between the two places a biography can live: English on the artist row (because that
/// text feeds the embedding — D62), every other language in an <see cref="ArtistBiography"/> row.
/// Both halves of it are places a bug would be silent rather than loud, which is what these bite on:
/// a wrong <see cref="ArtistBiographies.PendingLanguages"/> either re-asks a free API for ever or
/// never asks at all, and neither shows up as an error.
/// </summary>
public class ArtistBiographiesTests
{
    private static readonly string[] EnEs = ["en", "es"];

    // Carries an Mbid, because a band without one is unmatchable by our accurate rule and every
    // PendingPredicate assertion below would pass for that reason instead of the one under test.
    private static Artist Band() => new() { Id = Guid.NewGuid(), Mbid = Guid.NewGuid(), Name = "Darkthrone" };

    // --- PendingLanguages ---

    [Fact]
    public void PendingLanguages_FreshArtist_NeedsEveryLanguage()
    {
        Assert.Equal(["en", "es"], ArtistBiographies.PendingLanguages(Band(), EnEs));
    }

    [Fact]
    public void PendingLanguages_EnglishAlreadyStamped_StillNeedsSpanish()
    {
        // THE bug this whole design exists to avoid. abstract_checked_at is set on 206 882 rows; if
        // Spanish read English's marker it would find the catalogue "done" and visit nobody, and the
        // feature would ship looking finished while having fetched nothing.
        Artist artist = Band();
        artist.AbstractCheckedAt = DateTime.UtcNow;

        Assert.Equal(["es"], ArtistBiographies.PendingLanguages(artist, EnEs));
    }

    [Fact]
    public void PendingLanguages_EnglishMatchedButNeverStamped_IsStillDone()
    {
        // Text is its own proof we asked — the same rule listeners_checked_at was given (D61).
        Artist artist = Band();
        artist.Abstract = "Darkthrone are a Norwegian black metal band.";

        Assert.Equal(["es"], ArtistBiographies.PendingLanguages(artist, EnEs));
    }

    [Fact]
    public void PendingLanguages_CheckedSpanishMissCountsAsChecked()
    {
        // A row with no text is a recorded gap, not an absence of work: eswiki genuinely has no
        // article. Re-asking would spend a request on a free service to learn what we know.
        Artist artist = Band();
        artist.AbstractCheckedAt = DateTime.UtcNow;
        artist.Biographies.Add(new ArtistBiography { Language = "es", Abstract = null, CheckedAt = DateTime.UtcNow });

        Assert.Empty(ArtistBiographies.PendingLanguages(artist, EnEs));
    }

    [Fact]
    public void PendingLanguages_MatchedSpanishCountsAsChecked()
    {
        Artist artist = Band();
        artist.Abstract = "English text.";
        artist.Biographies.Add(new ArtistBiography
        {
            Language = "es",
            Abstract = "Texto en español.",
            AbstractUrl = "https://es.wikipedia.org/wiki/Darkthrone",
            CheckedAt = DateTime.UtcNow,
        });

        Assert.Empty(ArtistBiographies.PendingLanguages(artist, EnEs));
    }

    [Fact]
    public void PendingLanguages_ANewLanguageWalksTheCorpusWithoutDisturbingTheOthers()
    {
        // Switching 'no' on must make a fully-resolved artist pending again in Norwegian ONLY — that
        // is what makes adding a language configuration rather than a migration.
        Artist artist = Band();
        artist.Abstract = "English text.";
        artist.Biographies.Add(new ArtistBiography { Language = "es", CheckedAt = DateTime.UtcNow });

        Assert.Equal(["no"], ArtistBiographies.PendingLanguages(artist, ["en", "es", "no"]));
    }

    [Fact]
    public void PendingLanguages_IsCaseInsensitiveOnTheStoredCode()
    {
        Artist artist = Band();
        artist.AbstractCheckedAt = DateTime.UtcNow;
        artist.Biographies.Add(new ArtistBiography { Language = "ES", CheckedAt = DateTime.UtcNow });

        Assert.Empty(ArtistBiographies.PendingLanguages(artist, EnEs));
    }

    // --- PendingPredicate ---

    /// <summary>
    /// A context that is never connected: ToQueryString compiles the expression to SQL without
    /// opening a socket, so this asks "does PostgreSQL understand this?" and nothing else.
    /// </summary>
    private static GrimoireDbContext OfflineContext()
    {
        DbContextOptionsBuilder<GrimoireDbContext> options = new();
        options.UseNpgsql("Host=127.0.0.1;Database=none;Username=none;Password=none", o => o.UseVector())
            .UseSnakeCaseNamingConvention();

        return new GrimoireDbContext(options.Options);
    }

    [Fact]
    public void PendingPredicate_TranslatesToSqlAndKeepsTheAntiJoinInTheDatabase()
    {
        // The failure this catches has no other warning: an untranslatable predicate throws at first
        // use, in the worker, against the live catalogue — or worse, silently evaluates client-side
        // and drags 206 887 rows carrying a 768-dimension embedding into memory to filter them (the
        // exact mistake D61 found in ListenersJob).
        using GrimoireDbContext db = OfflineContext();

        string sql = db.Artists.Where(ArtistBiographies.PendingPredicate(["en", "es"])).ToQueryString();

        Assert.Contains("artist_biographies", sql, StringComparison.Ordinal);
        Assert.Contains("abstract_checked_at", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingPredicate_EnglishOnly_StillTranslates()
    {
        // The degenerate configuration: no child-table languages at all, so `wanted` is 0 and only
        // the English half can select anyone.
        using GrimoireDbContext db = OfflineContext();

        string sql = db.Artists.Where(ArtistBiographies.PendingPredicate(["en"])).ToQueryString();

        Assert.Contains("abstract_checked_at", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingPredicate_AgreesWithPendingLanguages_OnAFullyResolvedArtist()
    {
        // The coupling that matters: pass the predicate, and PendingLanguages must have something to
        // ask. A row that passes one and not the other is fetched every run, asked nothing, and never
        // resolves — an infinite loop that looks like a working pass.
        Artist done = Band();
        done.Abstract = "English text.";
        done.AbstractCheckedAt = DateTime.UtcNow;
        done.Biographies.Add(new ArtistBiography { Language = "es", CheckedAt = DateTime.UtcNow });

        Func<Artist, bool> pending = ArtistBiographies.PendingPredicate(EnEs).Compile();

        Assert.False(pending(done));
        Assert.Empty(ArtistBiographies.PendingLanguages(done, EnEs));
    }

    [Fact]
    public void PendingPredicate_AgreesWithPendingLanguages_OnAnArtistOwedSpanish()
    {
        Artist owed = Band();
        owed.Abstract = "English text.";
        owed.AbstractCheckedAt = DateTime.UtcNow;

        Func<Artist, bool> pending = ArtistBiographies.PendingPredicate(EnEs).Compile();

        Assert.True(pending(owed));
        Assert.Equal(["es"], ArtistBiographies.PendingLanguages(owed, EnEs));
    }

    [Fact]
    public void PendingPredicate_NormalisesConfiguredCasing_SoItCannotDisagreeWithPendingLanguages()
    {
        // "ES" in configuration against "es" in the database: `= ANY` is case-sensitive where
        // PendingLanguages is not. Un-normalised, this artist is pending for ever and asked nothing.
        Artist artist = Band();
        artist.Abstract = "English text.";
        artist.Biographies.Add(new ArtistBiography { Language = "es", CheckedAt = DateTime.UtcNow });

        string[] shouty = ["EN", "ES"];
        Func<Artist, bool> pending = ArtistBiographies.PendingPredicate(shouty).Compile();

        Assert.False(pending(artist));
        Assert.Empty(ArtistBiographies.PendingLanguages(artist, shouty));
    }

    [Fact]
    public void PendingPredicate_SkipsArtistsWithNoMbid()
    {
        // The member rows: no MusicBrainz id means no accurate match is even possible (D22 homonyms).
        Artist member = Band();
        member.Mbid = Guid.Empty;

        Assert.False(ArtistBiographies.PendingPredicate(EnEs).Compile()(member));
    }

    // --- Merge ---

    [Fact]
    public void Merge_ReadsEnglishOffTheArtistRowAndTheRestOffTheChildRows()
    {
        Artist artist = Band();
        artist.Abstract = "English text.";
        artist.AbstractUrl = "https://en.wikipedia.org/wiki/Darkthrone";
        artist.Biographies.Add(new ArtistBiography
        {
            Language = "es",
            Abstract = "Texto en español.",
            AbstractUrl = "https://es.wikipedia.org/wiki/Darkthrone",
            CheckedAt = DateTime.UtcNow,
        });

        IReadOnlyList<ArtistBiographyView> merged = ArtistBiographies.Merge(artist);

        Assert.Equal(2, merged.Count);
        Assert.Equal("en", merged[0].Language);
        Assert.Equal("English text.", merged[0].Abstract);
        Assert.Equal("es", merged[1].Language);
        Assert.Equal("https://es.wikipedia.org/wiki/Darkthrone", merged[1].Url);
    }

    [Fact]
    public void Merge_OmitsCheckedButAbsentLanguages()
    {
        // Merge returns what can be SHOWN, not what was searched: a checked miss is a marker, and
        // leaking it would render an empty Spanish biography over the English one that exists.
        Artist artist = Band();
        artist.Abstract = "English text.";
        artist.Biographies.Add(new ArtistBiography { Language = "es", Abstract = null, CheckedAt = DateTime.UtcNow });

        ArtistBiographyView single = Assert.Single(ArtistBiographies.Merge(artist));

        Assert.Equal("en", single.Language);
    }

    [Fact]
    public void Merge_SpanishOnlyBand_IsReturnedWithNoEnglishEntry()
    {
        Artist artist = Band();
        artist.AbstractCheckedAt = DateTime.UtcNow;
        artist.Biographies.Add(new ArtistBiography
        {
            Language = "es",
            Abstract = "Banda española de rock.",
            AbstractUrl = "https://es.wikipedia.org/wiki/Héroes_del_Silencio",
            CheckedAt = DateTime.UtcNow,
        });

        ArtistBiographyView single = Assert.Single(ArtistBiographies.Merge(artist));

        Assert.Equal("es", single.Language);
    }

    [Fact]
    public void Merge_IgnoresAStrayEnglishChildRow()
    {
        // English belongs on the artist row. A row that says otherwise must never shadow it, or the
        // page could show two different English biographies depending on ordering.
        Artist artist = Band();
        artist.Abstract = "The canonical English text.";
        artist.Biographies.Add(new ArtistBiography { Language = "en", Abstract = "An impostor.", CheckedAt = DateTime.UtcNow });

        ArtistBiographyView single = Assert.Single(ArtistBiographies.Merge(artist));

        Assert.Equal("The canonical English text.", single.Abstract);
    }

    [Fact]
    public void Merge_OrdersEnglishFirstThenByLanguageCode()
    {
        // "Whatever is available" has to be a reproducible choice, not whatever Postgres returned.
        Artist artist = Band();
        artist.Abstract = "English text.";
        artist.Biographies.Add(new ArtistBiography { Language = "sv", Abstract = "Svensk text.", CheckedAt = DateTime.UtcNow });
        artist.Biographies.Add(new ArtistBiography { Language = "no", Abstract = "Norsk tekst.", CheckedAt = DateTime.UtcNow });

        Assert.Equal(["en", "no", "sv"], ArtistBiographies.Merge(artist).Select(b => b.Language));
    }

    [Fact]
    public void Merge_NoBiographyAnywhere_IsEmpty()
    {
        // The common case: most of the underground has no article in any language. A gap, not an error.
        Assert.Empty(ArtistBiographies.Merge(Band()));
    }

    [Fact]
    public void Merge_BlankEnglishTextIsNotABiography()
    {
        Artist artist = Band();
        artist.Abstract = "   ";

        Assert.Empty(ArtistBiographies.Merge(artist));
    }
}
