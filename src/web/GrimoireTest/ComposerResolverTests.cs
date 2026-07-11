using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class ComposerResolverTests
{
    private static ComposerCandidate Person(string id, string name, string? sortName = null, params string[] aliases)
    {
        return new ComposerCandidate(id, name, sortName, "Person", aliases);
    }

    [Fact]
    public void ExactPrimaryName_Resolves()
    {
        ComposerMatch m = ComposerResolver.Resolve("Ludwig van Beethoven",
        [
            Person("beet", "Ludwig van Beethoven"),
        ]);

        Assert.Equal(ComposerMatchStatus.Resolved, m.Status);
        Assert.Equal("beet", m.Mbid);
    }

    [Fact]
    public void MatchesOnAlias_WhenPrimaryIsNativeSpelling()
    {
        // MusicBrainz stores Bartók under the Hungarian order "Bartók Béla"; the common form is an
        // alias. Recognising the entity by an alias is honest, not a guess.
        ComposerMatch m = ComposerResolver.Resolve("Béla Bartók",
        [
            Person("bart", "Bartók Béla", "Bartók, Béla", "Béla Bartók", "B. Bartók"),
        ]);

        Assert.Equal(ComposerMatchStatus.Resolved, m.Status);
        Assert.Equal("bart", m.Mbid);
    }

    [Fact]
    public void DiacriticsAreIgnored()
    {
        ComposerMatch m = ComposerResolver.Resolve("Arnold Schonberg",
        [
            Person("scho", "Arnold Schönberg"),
        ]);

        Assert.Equal(ComposerMatchStatus.Resolved, m.Status);
        Assert.Equal("scho", m.Mbid);
    }

    [Fact]
    public void MatchesOnSortName()
    {
        ComposerMatch m = ComposerResolver.Resolve("Chopin, Fryderyk",
        [
            Person("cho", "Fryderyk Chopin", "Chopin, Fryderyk"),
        ]);

        Assert.Equal(ComposerMatchStatus.Resolved, m.Status);
        Assert.Equal("cho", m.Mbid);
    }

    [Fact]
    public void TwoDistinctPeople_AreAmbiguous_NotGuessed()
    {
        // Two different Persons named "Richard Wagner": we skip rather than pick one.
        ComposerMatch m = ComposerResolver.Resolve("Richard Wagner",
        [
            Person("composer", "Richard Wagner"),
            Person("other", "Richard Wagner"),
        ]);

        Assert.Equal(ComposerMatchStatus.Ambiguous, m.Status);
        Assert.Null(m.Mbid);
    }

    [Fact]
    public void SameEntityTwice_IsNotAmbiguous()
    {
        // The same MBID appearing twice in the results is one entity, so it still resolves.
        ComposerMatch m = ComposerResolver.Resolve("Claude Debussy",
        [
            Person("deb", "Claude Debussy"),
            Person("deb", "Claude Debussy", "Debussy, Claude"),
        ]);

        Assert.Equal(ComposerMatchStatus.Resolved, m.Status);
        Assert.Equal("deb", m.Mbid);
    }

    [Fact]
    public void NoNameMatch_IsNotFound()
    {
        ComposerMatch m = ComposerResolver.Resolve("Igor Stravinsky",
        [
            Person("other", "Povilas Stravinsky"),
        ]);

        Assert.Equal(ComposerMatchStatus.NotFound, m.Status);
        Assert.Null(m.Mbid);
    }

    [Fact]
    public void NonPersonWithMatchingName_IsExcluded()
    {
        // The "Arnold Schoenberg Chor" is a Choir, not the composer: it must not resolve.
        ComposerMatch m = ComposerResolver.Resolve("Arnold Schoenberg Chor",
        [
            new ComposerCandidate("chor", "Arnold Schoenberg Chor", null, "Choir", []),
        ]);

        Assert.Equal(ComposerMatchStatus.NotFound, m.Status);
    }
}
