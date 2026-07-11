using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The Dark Twin ranking (feature B18). These bite on the score that defines the twin: close taste
/// AND disjoint collection wins; a taste-alike who shares your whole grimoire, or a disjoint stranger
/// with alien taste, both lose. Too few users → no twin (the honest empty state).
/// </summary>
public class DarkTwinMathTests
{
    private static Guid G(int n)
    {
        byte[] b = new byte[16];
        b[0] = (byte)n;
        return new Guid(b);
    }

    [Fact]
    public void Best_PrefersCloseTasteAndDisjointCollection()
    {
        float[] me = [1f, 0f];
        HashSet<Guid> mine = [G(1), G(2)];

        // Twin A: close taste, fully disjoint collection → the ideal twin.
        var a = new DarkTwinMath.Candidate(G(10), [0.99f, 0.01f], new HashSet<Guid> { G(3), G(4) });
        // Twin B: close taste, but same collection as me → nothing to offer.
        var b = new DarkTwinMath.Candidate(G(11), [0.99f, 0.01f], new HashSet<Guid> { G(1), G(2) });
        // Twin C: fully disjoint, but alien taste → not really a twin.
        var c = new DarkTwinMath.Candidate(G(12), [0f, 1f], new HashSet<Guid> { G(5), G(6) });

        DarkTwinMath.TwinResult? best = DarkTwinMath.Best(me, mine, [a, b, c]);

        Assert.NotNull(best);
        Assert.Equal(G(10), best.Value.UserId);
        Assert.Equal(1.0, best.Value.Disjointness, 6);
    }

    [Fact]
    public void Best_SharedCollectionScoresLowerThanDisjoint()
    {
        float[] me = [1f, 0f];
        HashSet<Guid> mine = [G(1), G(2)];

        var shared = new DarkTwinMath.Candidate(G(11), [0.99f, 0.01f], new HashSet<Guid> { G(1), G(2) });
        var disjoint = new DarkTwinMath.Candidate(G(10), [0.9f, 0.1f], new HashSet<Guid> { G(3), G(4) });

        DarkTwinMath.TwinResult? best = DarkTwinMath.Best(me, mine, [shared, disjoint]);

        // Even though `shared` is slightly closer in taste, its zero disjointness zeroes the score.
        Assert.Equal(G(10), best!.Value.UserId);
    }

    [Fact]
    public void Best_NoCandidates_IsNull()
    {
        Assert.Null(DarkTwinMath.Best([1f, 0f], new HashSet<Guid> { G(1) }, []));
    }

    [Fact]
    public void Best_CandidateWithNoOverlapAndNoCollection_IsSkipped()
    {
        // Both sides empty → union 0 → disjointness undefined → skipped, leaving no twin.
        var empty = new DarkTwinMath.Candidate(G(10), [1f, 0f], new HashSet<Guid>());

        Assert.Null(DarkTwinMath.Best([1f, 0f], new HashSet<Guid>(), [empty]));
    }

    [Fact]
    public void Best_EmptyCollectionTwin_IsSkippedEvenWhenIHaveOne()
    {
        // A twin with an empty grimoire has nothing to offer; its trivial disjointness of 1.0 must
        // NOT let it beat a twin with a real, disjoint collection. This guards the empty-theirsOnly bug.
        float[] me = [1f, 0f];
        HashSet<Guid> mine = [G(1), G(2)];

        var emptyButClose = new DarkTwinMath.Candidate(G(10), [1f, 0f], new HashSet<Guid>());
        var realOffer = new DarkTwinMath.Candidate(G(11), [0.8f, 0.2f], new HashSet<Guid> { G(3), G(4) });

        DarkTwinMath.TwinResult? best = DarkTwinMath.Best(me, mine, [emptyButClose, realOffer]);

        Assert.Equal(G(11), best!.Value.UserId);
    }

    [Fact]
    public void Best_OnlyEmptyCollectionCandidates_IsNull()
    {
        // If every other user has an empty grimoire, there is no twin worth naming.
        var a = new DarkTwinMath.Candidate(G(10), [1f, 0f], new HashSet<Guid>());
        var b = new DarkTwinMath.Candidate(G(11), [0.9f, 0.1f], new HashSet<Guid>());

        Assert.Null(DarkTwinMath.Best([1f, 0f], new HashSet<Guid> { G(1) }, [a, b]));
    }

    [Fact]
    public void Best_TieOnScore_BreaksOnSmallerUserId()
    {
        float[] me = [1f, 0f];
        HashSet<Guid> mine = [G(1)];

        // Two identical candidates but different ids: the smaller id must win, deterministically.
        var hi = new DarkTwinMath.Candidate(G(20), [0.9f, 0.1f], new HashSet<Guid> { G(2) });
        var lo = new DarkTwinMath.Candidate(G(10), [0.9f, 0.1f], new HashSet<Guid> { G(2) });

        DarkTwinMath.TwinResult? best = DarkTwinMath.Best(me, mine, [hi, lo]);

        Assert.Equal(G(10), best!.Value.UserId);
    }
}
