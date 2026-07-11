using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Rare-instrument detection (feature C15). These bite on the boundary between the standard rock
/// kit (never rare) and the folk/orchestral colour the feature exists to surface, plus the traps
/// that a naive substring check would get wrong (bass drum, whistling vs tin whistle).
/// </summary>
public class InstrumentClassifierTests
{
    [Theory]
    // The standard rock kit and its variants are NOT rare.
    [InlineData("guitar")]
    [InlineData("electric guitar")]
    [InlineData("12 string guitar")]
    [InlineData("bass guitar")]
    [InlineData("electric bass guitar")]
    [InlineData("drums (drum set)")]
    [InlineData("lead vocals")]
    [InlineData("background vocals")]
    [InlineData("keyboard")]
    [InlineData("synthesizer")]
    [InlineData("Hammond organ")]
    [InlineData("grand piano")]
    // Common studio percussion / effects are NOT rare.
    [InlineData("percussion")]
    [InlineData("tambourine")]
    [InlineData("handclaps")]
    [InlineData("whistling")] // a vocal effect, not the folk whistle
    // Absent instruments are not rare.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsRare_False_ForStandardKitStudioAndAbsent(string? instrument)
    {
        Assert.False(InstrumentClassifier.IsRare(instrument));
    }

    [Theory]
    // The folk/orchestral colour C15 is looking for IS rare.
    [InlineData("bagpipe")]
    [InlineData("shawm")]
    [InlineData("hurdy gurdy")]
    [InlineData("uilleann pipes")]
    [InlineData("fiddle")]
    [InlineData("violin")]
    [InlineData("cello")]
    [InlineData("mandolin")]
    [InlineData("accordion")]
    [InlineData("tin whistle")] // the folk whistle, distinct from "whistling"
    [InlineData("bodhrán")]
    [InlineData("talharpa")]
    [InlineData("oboe")]
    public void IsRare_True_ForFolkAndOrchestralInstruments(string instrument)
    {
        Assert.True(InstrumentClassifier.IsRare(instrument));
    }

    [Fact]
    public void IsRare_TreatsTinWhistleAndWhistlingDifferently()
    {
        // The trap the substring approach must not fall into: "whistling" (vocal) excluded,
        // "tin whistle" (folk instrument) kept. Inverting either breaks this pair.
        Assert.False(InstrumentClassifier.IsRare("whistling"));
        Assert.True(InstrumentClassifier.IsRare("tin whistle"));
    }
}
