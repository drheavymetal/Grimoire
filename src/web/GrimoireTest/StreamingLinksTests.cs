using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class StreamingLinksTests
{
    [Fact]
    public void SearchServices_AreAlwaysPresent_AndEncoded()
    {
        Dictionary<string, string> links = StreamingLinks.Build("Old Man's Child", null, null);

        Assert.Equal("https://open.spotify.com/search/Old%20Man%27s%20Child", links[StreamingLinks.SpotifyKey]);
        Assert.Contains(StreamingLinks.YouTubeMusicKey, links.Keys);
        Assert.Contains(StreamingLinks.TidalKey, links.Keys);
        Assert.Contains(StreamingLinks.BandcampKey, links.Keys);
    }

    [Fact]
    public void ExactLinks_AddedOnlyWhenResolved()
    {
        Dictionary<string, string> without = StreamingLinks.Build("Darkthrone", null, null);
        Assert.False(without.ContainsKey(StreamingLinks.AppleMusicKey));
        Assert.False(without.ContainsKey(StreamingLinks.DeezerKey));

        Dictionary<string, string> with = StreamingLinks.Build(
            "Darkthrone",
            "https://music.apple.com/us/artist/darkthrone/112417640",
            "https://www.deezer.com/artist/7641");

        Assert.Equal("https://music.apple.com/us/artist/darkthrone/112417640", with[StreamingLinks.AppleMusicKey]);
        Assert.Equal("https://www.deezer.com/artist/7641", with[StreamingLinks.DeezerKey]);
    }

    [Fact]
    public void AllCuratedKeys_CarryTheListenPrefix_SoTheyNeverClashWithMbRels()
    {
        Dictionary<string, string> links = StreamingLinks.Build("Band", "apple", "deezer");

        Assert.All(links.Keys, k => Assert.StartsWith(StreamingLinks.Prefix, k));
    }

    [Fact]
    public void EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => StreamingLinks.Build("  ", null, null));
    }
}
