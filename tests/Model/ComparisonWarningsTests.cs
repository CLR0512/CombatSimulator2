using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ComparisonWarningsTests
{
    private static Run Side(string build, string? mvid) => new() { GameBuild = build, Cs2Mvid = mvid };

    [Fact]
    public void SameBuildsWarnOfNothing() => Assert.Empty(ComparisonWarnings.Of(Side("1", "m"), Side("1", "m"), "m"));

    [Fact]
    public void OtherGameBuildNamesBoth()
    {
        var note = Assert.Single(ComparisonWarnings.Of(Side("100", "m"), Side("101", "m"), "m"));
        Assert.Contains("100", note.English(), StringComparison.Ordinal);
        Assert.Contains("101", note.English(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("old")]
    [InlineData(null)]
    public void EarlierOrUnknownCs2Build(string? mvid) =>
        Assert.Contains(
            "earlier version",
            Assert.Single(ComparisonWarnings.Of(Side("1", mvid), Side("1", "m"), "m")).English(),
            StringComparison.Ordinal
        );

    [Fact]
    public void BothDifferencesGiveTwoNotes() =>
        Assert.Equal(2, ComparisonWarnings.Of(Side("1", "old"), Side("2", "m"), "m").Count);
}
