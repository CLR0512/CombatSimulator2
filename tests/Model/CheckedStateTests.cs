using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class CheckedStateTests
{
    [Fact]
    public void ListedWhenBuildAndVersionMatch()
    {
        var state = CheckedState.Of("26724394", "0.11.6-alpha.1", "0.11.6-alpha.1");
        Assert.Equal(CheckedKind.Listed, state.Kind);
        Assert.False(state.Shows);
        Assert.Equal("game build 26724394 0.11.6-alpha.1: checked", state.StatusLine);
    }

    // A listed build id whose version text differs is another game: Steam's id alone doesn't decide.
    [Fact]
    public void UnlistedWhenTheListedBuildHasAnotherVersion()
    {
        var state = CheckedState.Of("26724394", "0.11.6-alpha.1", "0.11.7-alpha");
        Assert.Equal(CheckedKind.Unlisted, state.Kind);
        Assert.True(state.Shows);
        Assert.Equal("game build 26724394 0.11.7-alpha: not checked", state.StatusLine);
    }

    [Fact]
    public void UnlistedWhenTheBuildIsNotListed()
    {
        var state = CheckedState.Of("1", null, "0.11.6-alpha.1");
        Assert.Equal(CheckedKind.Unlisted, state.Kind);
        Assert.True(state.Shows);
    }

    [Fact]
    public void UnknownWhenTheBuildCannotBeRead()
    {
        var state = CheckedState.Of(CheckedState.UnknownBuild, null, "0.11.6-alpha.1");
        Assert.Equal(CheckedKind.Unknown, state.Kind);
        Assert.True(state.Shows);
        Assert.Equal("game build unknown 0.11.6-alpha.1: unknown", state.StatusLine);
    }

    // Before the game's version text can be read, a listed build counts as checked rather than warning early.
    [Fact]
    public void BuildAloneDecidesBeforeTheVersionIsKnown()
    {
        Assert.Equal(CheckedKind.Listed, CheckedState.Of("26724394", "0.11.6-alpha.1", null).Kind);
        Assert.Equal(CheckedKind.Unlisted, CheckedState.Of("1", null, null).Kind);
        Assert.Equal("game build 1 ?: not checked", CheckedState.Of("1", null, null).StatusLine);
    }
}
