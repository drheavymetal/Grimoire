using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class EmbeddingTextBuilderTests
{
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
