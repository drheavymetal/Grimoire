using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class EmbeddingTextBuilderTests
{
    /// <summary>
    /// The fingerprint is what tells the embedding pass a stored vector has gone stale. If it did not
    /// move when the text moves, enrichment (new tags, a new biography) would leave the catalogue
    /// describing bands as they looked the day they were first embedded.
    /// </summary>
    [Fact]
    public void Fingerprint_IsStableForTheSameText()
    {
        Assert.Equal(
            EmbeddingTextBuilder.Fingerprint("Darkthrone. Genres: black metal."),
            EmbeddingTextBuilder.Fingerprint("Darkthrone. Genres: black metal."));
    }

    [Fact]
    public void Fingerprint_MovesWhenTheTextMoves()
    {
        Assert.NotEqual(
            EmbeddingTextBuilder.Fingerprint("Darkthrone. Genres: black metal."),
            EmbeddingTextBuilder.Fingerprint("Darkthrone. Genres: black metal, death metal."));
    }

    [Fact]
    public void Fingerprint_GainingABiography_ChangesTheFingerprint()
    {
        Artist band = new()
        {
            Name = "Darkthrone",
            Kind = ArtistKind.Group,
            Country = "NO",
            Tags = ["black metal"],
        };

        string before = EmbeddingTextBuilder.Fingerprint(EmbeddingTextBuilder.Build(band)!);

        band.Abstract = "Darkthrone is a Norwegian metal band formed in 1986.";
        string after = EmbeddingTextBuilder.Fingerprint(EmbeddingTextBuilder.Build(band)!);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Fingerprint_IsThirtyTwoHexCharacters()
    {
        string fingerprint = EmbeddingTextBuilder.Fingerprint("Darkthrone.");

        Assert.Equal(32, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{32}$", fingerprint);
    }

    [Fact]
    public void RichArtist_IncludesNameTagsPlaceAndMembers()
    {
        Artist artist = new()
        {
            Name = "Darkthrone",
            Kind = ArtistKind.Group,
            Country = "NO",
            City = "Kolbotn",
            Tags = ["black metal", "death metal"],
        };

        string? text = EmbeddingTextBuilder.Build(artist, ["Fenriz", "Nocturno Culto"]);

        Assert.NotNull(text);
        Assert.Contains("Darkthrone", text);
        Assert.Contains("black metal, death metal", text);
        Assert.Contains("Kolbotn, NO", text);
        Assert.Contains("Fenriz, Nocturno Culto", text);
    }

    [Fact]
    public void AbstractAndLabels_AreIncludedWhenPresent()
    {
        Artist artist = new()
        {
            Name = "Wardruna",
            Kind = ArtistKind.Group,
            Tags = ["nordic folk"],
            Abstract = "Norwegian music group inspired by Norse tradition.",
        };

        string? text = EmbeddingTextBuilder.Build(artist, null, ["Indie Recordings"]);

        Assert.NotNull(text);
        Assert.Contains("Labels: Indie Recordings", text);
        Assert.Contains("Norse tradition", text!);
    }

    [Fact]
    public void NoSignal_ReturnsNull_SoEmbeddingStaysNull()
    {
        // A bare member row: identity only. It carries no discovery signal, so no vector.
        Artist artist = new() { Name = "Some Session Drummer", Kind = ArtistKind.Person };

        Assert.Null(EmbeddingTextBuilder.Build(artist));
    }

    [Fact]
    public void MembersAlone_CountAsSignal()
    {
        Artist artist = new() { Name = "Obscure Band", Kind = ArtistKind.Group };

        string? text = EmbeddingTextBuilder.Build(artist, ["Only Member"]);

        Assert.NotNull(text);
        Assert.Contains("Members: Only Member", text!);
    }

    [Fact]
    public void PersonAndGroup_PhraseThePlaceDifferently()
    {
        Artist person = new() { Name = "Einar Selvik", Kind = ArtistKind.Person, Country = "NO" };
        Artist group = new() { Name = "Wardruna", Kind = ArtistKind.Group, Country = "NO" };

        Assert.Contains("From NO", EmbeddingTextBuilder.Build(person)!);
        Assert.Contains("Group from NO", EmbeddingTextBuilder.Build(group)!);
    }
}
