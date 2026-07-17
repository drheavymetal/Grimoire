using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The typed-name judge of the Hard difficulty (D67). The brief had two sides and they pull against
/// each other: failing somebody over a missing accent turns a music game into a typing test, while
/// accepting the name of a DIFFERENT band stops the game measuring anything at all. These tests hold
/// both ends at once — that is the whole reason they exist.
/// </summary>
public class GuessMatchTests
{
    private static readonly string[] Nothing = [];

    // -----------------------------------------------------------------------
    // Gate 1: accents, case and spacing are not the test
    // -----------------------------------------------------------------------

    /// <summary>
    /// The frustration case, and it is settled before any distance is measured — so no threshold, now
    /// or later, can bring it back. These fold away by normalisation (the same rule the preview matcher
    /// has always used), which is why an accent is free even on a four-letter name with no edit budget.
    /// </summary>
    [Theory]
    [InlineData("skald", "SKÁLD")]
    [InlineData("SKALD", "SKÁLD")]
    [InlineData("motorhead", "Motörhead")]
    [InlineData("MOTÖRHEAD", "Motörhead")]
    [InlineData("darkthrone", "Darkthrone")]
    [InlineData("  Darkthrone  ", "Darkthrone")]
    [InlineData("old man's  child", "Old Man's Child")]
    [InlineData("BLIND GUARDIAN", "Blind Guardian")]
    public void Accents_Case_AndSpacing_AreForgiven(string typed, string answer)
    {
        Assert.True(GuessMatch.IsCorrect(typed, answer, Nothing));
    }

    /// <summary>Nothing typed is not an answer, and neither is whitespace.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyGuess_IsNeverCorrect(string? typed)
    {
        Assert.False(GuessMatch.IsCorrect(typed, "Darkthrone", Nothing));
    }

    // -----------------------------------------------------------------------
    // Gate 3: typos, within a budget that scales with the name
    // -----------------------------------------------------------------------

    /// <summary>The typo case from D67's own text, and its neighbours: one slip on a real name.</summary>
    [Theory]
    [InlineData("darkthron", "Darkthrone")]
    [InlineData("darkthrone ", "Darkthrone")]
    [InlineData("burzum ", "Burzum")]
    [InlineData("burzun", "Burzum")]
    [InlineData("Bathori", "Bathory")]
    public void ASingleSlip_IsForgiven(string typed, string answer)
    {
        Assert.True(GuessMatch.IsCorrect(typed, answer, Nothing));
    }

    /// <summary>
    /// Swapped letters are ONE slip, not two — which is only true because the distance counts
    /// transpositions as the single action they are. Under plain Levenshtein every one of these costs
    /// two edits and fails its budget, and "darkthorne" being marked wrong for "Darkthrone" is exactly
    /// the frustration this mode was warned about. Revert the distance to Levenshtein and this test is
    /// what fails.
    /// </summary>
    [Theory]
    [InlineData("darkthorne", "Darkthrone")]
    [InlineData("mayehm", "Mayhem")]
    [InlineData("Blind Guardain", "Blind Guardian")]
    public void SwappedLetters_CountAsOneSlip(string typed, string answer)
    {
        Assert.True(GuessMatch.IsCorrect(typed, answer, Nothing));
    }

    /// <summary>A long name is more to mistype and its neighbourhood is emptier, so it gets two.</summary>
    [Theory]
    [InlineData("Dimmu Borgier", "Dimmu Borgir")]
    [InlineData("Wolves in the Throne Roam", "Wolves in the Throne Room")]
    public void ALongName_ForgivesTwo(string typed, string answer)
    {
        Assert.True(GuessMatch.IsCorrect(typed, answer, Nothing));
    }

    /// <summary>
    /// Short names get NO budget, and that is deliberate rather than mean. At four characters one edit
    /// reaches half the language: "Tool" is one step from "Toon", "Cool", "Toad" and "Took", and a
    /// budget there would accept a player who typed a different word entirely.
    /// </summary>
    [Theory]
    [InlineData("Toll", "Tool")]
    [InlineData("Coal", "Tool")]
    [InlineData("Sunn", "Sunо")]
    public void AShortName_MustBeExact(string typed, string answer)
    {
        Assert.False(GuessMatch.IsCorrect(typed, answer, Nothing));
    }

    /// <summary>...but a short name typed correctly is still correct. The budget is zero, not the gate.</summary>
    [Fact]
    public void AShortName_TypedRight_IsCorrect()
    {
        Assert.True(GuessMatch.IsCorrect("tool", "Tool", Nothing));
    }

    /// <summary>Generosity has an end. A wrong band typed perfectly is not a typo of the right one.</summary>
    [Theory]
    [InlineData("Toto", "Death")]
    [InlineData("Metallica", "Darkthrone")]
    [InlineData("Darkthrone Tribute", "Darkthrone")]
    [InlineData("Mayhemic", "Mayhem")]
    [InlineData("", "Mayhem")]
    // Two edits on a seven-letter name: past the budget, and correctly so. The line has to be
    // somewhere, and "Bathori" (one edit) is on the generous side of it.
    [InlineData("Bathorie", "Bathory")]
    public void ADifferentName_IsNotForgiven(string typed, string answer)
    {
        Assert.False(GuessMatch.IsCorrect(typed, answer, Nothing));
    }

    // -----------------------------------------------------------------------
    // Gate 2: the thing that must never happen — accepting another band
    // -----------------------------------------------------------------------

    /// <summary>
    /// THE rule. "Mayhemic" is a real band and it is not "Mayhem" — two edits apart, comfortably inside
    /// the trigram similarity any threshold loose enough to accept "darkthron" would allow, which is
    /// exactly why this judge does not use trigram similarity. It is refused with no other band in
    /// sight, and refused again when the catalogue confirms it exists.
    /// </summary>
    [Fact]
    public void ARealDifferentBand_IsRefused()
    {
        Assert.False(GuessMatch.IsCorrect("Mayhemic", "Mayhem", Nothing));
        Assert.False(GuessMatch.IsCorrect("Mayhemic", "Mayhem", ["Mayhemic"]));
    }

    /// <summary>
    /// Naming another band EXACTLY is a statement, not a slip — so no edit budget rescues it, however
    /// near it sits. "Immortals" is one edit from "Immortal": inside the budget, and still wrong,
    /// because the player named a band and it was the other one.
    /// </summary>
    [Fact]
    public void ExactlyNamingAnotherBand_LosesEvenWithinTheBudget()
    {
        // Alone, the typo reading is the only reading, and it is generously accepted.
        Assert.True(GuessMatch.IsCorrect("Immortals", "Immortal", Nothing));

        // With that band actually in reach, "Immortals" is a name, not a mistake.
        Assert.False(GuessMatch.IsCorrect("Immortals", "Immortal", ["Immortals"]));
    }

    /// <summary>An accent on ANOTHER band's name does not launder it into the answer either.</summary>
    [Fact]
    public void AnotherBand_IsRecognised_ThroughItsAccents()
    {
        Assert.False(GuessMatch.IsCorrect("Immortals", "Immortal", ["Ímmörtåls"]));
    }

    /// <summary>
    /// A tie is a failure. If a guess is an equally good reading of two real bands, the player did not
    /// name either of them, and calling it right would be the coin flip this mode exists to avoid.
    /// </summary>
    [Fact]
    public void AGuess_EquallyCloseToTwoBands_IsRefused()
    {
        // "Burzun" is one edit from Burzum and one from Burzuk: ambiguous, so no.
        Assert.False(GuessMatch.IsCorrect("Burzun", "Burzum", ["Burzuk"]));

        // With nothing else in reach, the same guess is an ordinary typo and passes.
        Assert.True(GuessMatch.IsCorrect("Burzun", "Burzum", Nothing));
    }

    /// <summary>
    /// The answer is judged first: an EXACT hit wins before any other band is consulted. Real
    /// catalogues repeat names (MusicBrainz has many bands called "Death"), so a namesake in the pool
    /// must never turn a perfect answer into an ambiguity.
    /// </summary>
    [Fact]
    public void AnExactAnswer_WinsEvenWhenANamesakeExists()
    {
        Assert.True(GuessMatch.IsCorrect("Death", "Death", ["Death"]));
        Assert.True(GuessMatch.IsCorrect("death", "Death", ["Death", "Deth", "Dearth"]));
    }

    /// <summary>Distant bands in reach are irrelevant: they were never a reading of what was typed.</summary>
    [Fact]
    public void UnrelatedBandsInReach_DoNotBlockAnHonestTypo()
    {
        Assert.True(GuessMatch.IsCorrect("darkthron", "Darkthrone", ["Toto", "Metallica", "Emperor", "Burzum"]));
    }

    // -----------------------------------------------------------------------
    // The pieces
    // -----------------------------------------------------------------------

    /// <summary>The budget scales with the name, and the steps are where the doc says they are.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    [InlineData(30, 2)]
    public void TheEditBudget_ScalesWithTheName(int length, int expected)
    {
        Assert.Equal(expected, GuessMatch.EditBudget(length));
    }

    /// <summary>
    /// The distance, including its ceiling. The cap must never make a SMALL distance look large — it
    /// may only refuse to count past the limit — or a real answer would be thrown out by an
    /// optimisation.
    /// </summary>
    [Theory]
    [InlineData("darkthrone", "darkthrone", 2, 0)]
    [InlineData("darkthron", "darkthrone", 2, 1)]
    [InlineData("mayhem", "mayhemic", 3, 2)]
    [InlineData("", "abc", 5, 3)]
    [InlineData("abc", "", 5, 3)]
    // A swap is one action. Plain Levenshtein says 2 here, and that is the bug this rule fixes.
    [InlineData("ca", "ac", 3, 1)]
    [InlineData("darkthorne", "darkthrone", 3, 1)]
    public void Distance_CountsEdits(string a, string b, int limit, int expected)
    {
        Assert.Equal(expected, GuessMatch.Distance(a, b, limit));
    }

    /// <summary>Past the ceiling it only promises "more than the limit", which is all any caller asks.</summary>
    [Fact]
    public void Distance_AbandonsPastTheLimit()
    {
        Assert.True(GuessMatch.Distance("toto", "darkthrone", 2) > 2);
        Assert.True(GuessMatch.Distance("a", "aaaaaaaaaaaaaaaaaaaa", 1) > 1);
    }

    /// <summary>Symmetric, as a distance must be — otherwise the judgement would depend on argument order.</summary>
    [Theory]
    [InlineData("mayhem", "mayhemic")]
    [InlineData("darkthron", "darkthrone")]
    [InlineData("burzum", "emperor")]
    public void Distance_IsSymmetric(string a, string b)
    {
        Assert.Equal(GuessMatch.Distance(a, b, 10), GuessMatch.Distance(b, a, 10));
    }
}
