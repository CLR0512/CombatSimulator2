using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class EarlierResultsTests
{
    private static Run RunOf(string id, int targets, RunState state = RunState.Finished)
    {
        var run = new Run { Id = id, State = state };
        run.Targets.AddRange(Enumerable.Range(0, targets).Select(n => $"t:{n}"));
        return run;
    }

    private static readonly Func<string, bool> Saved = _ => true;

    private static readonly Run Full = RunOf("run-full", 147);

    [Fact]
    public void AFullRunThenOneTargetPointsBackAtTheFullRun()
    {
        var earlier = EarlierResults.After(Full, "run-one", 1, null, Saved);
        Assert.Equal(new EarlierResults("run-full", 147), earlier);
    }

    [Fact]
    public void AnotherNarrowRunStillPointsAtTheFullRun()
    {
        var held = EarlierResults.After(Full, "run-one", 1, null, Saved);
        var next = EarlierResults.After(RunOf("run-one", 1), "run-two", 1, held, Saved);
        Assert.Equal("run-full", next!.RunId);
        // A wider one-off between them, still narrower than the full run, changes nothing either.
        var third = EarlierResults.After(RunOf("run-two", 1), "run-three", 5, next, Saved);
        Assert.Equal(new EarlierResults("run-full", 147), third);
    }

    [Fact]
    public void AWiderRunHasNothingEarlier()
    {
        Assert.Null(EarlierResults.After(RunOf("run-one", 1), "run-full-2", 147, null, Saved));
        var held = new EarlierResults("run-full", 147);
        Assert.Null(EarlierResults.After(RunOf("run-one", 1), "run-wider", 148, held, Saved));
    }

    [Fact]
    public void TheSameNumberOfTargetsHasNothingEarlier()
    {
        Assert.Null(EarlierResults.After(Full, "run-full-2", 147, null, Saved));
        Assert.Null(EarlierResults.After(RunOf("run-one", 1), "run-again", 147, new("run-full", 147), Saved));
    }

    [Fact]
    public void ShowingTheEarlierRunAgainClearsIt()
    {
        var held = new EarlierResults("run-full", 147);
        Assert.Null(EarlierResults.After(RunOf("run-one", 1), "run-full", 147, held, Saved));
    }

    [Fact]
    public void AnEarlierRunThatWasDeletedIsForgotten()
    {
        var held = new EarlierResults("run-full", 147);
        Func<string, bool> gone = id => id != "run-full";
        // The run just replaced is wider and saved, so it takes over; with nothing wider there is none.
        Assert.Equal(
            new EarlierResults("run-mid", 20),
            EarlierResults.After(RunOf("run-mid", 20), "run-two", 1, held, gone)
        );
        Assert.Null(EarlierResults.After(RunOf("run-one", 1), "run-two", 1, held, gone));
    }

    [Fact]
    public void TheFirstRunOfASessionHasNothingEarlier()
    {
        Assert.Null(EarlierResults.After(null, "run-one", 1, null, Saved));
    }

    [Theory]
    [InlineData(RunState.Running)]
    [InlineData(RunState.Starting)]
    public void ARunThatWasNotFinalIsNotKeptAsEarlier(RunState state)
    {
        Assert.Null(EarlierResults.After(RunOf("run-full", 147, state), "run-one", 1, null, Saved));
    }

    [Fact]
    public void ACancelledRunThatWasSavedCounts()
    {
        var cancelled = RunOf("run-full", 147, RunState.Cancelled);
        Assert.Equal("run-full", EarlierResults.After(cancelled, "run-one", 1, null, Saved)!.RunId);
    }

    [Fact]
    public void ARunThatWasNeverSavedIsNotKeptAsEarlier()
    {
        Assert.Null(EarlierResults.After(Full, "run-one", 1, null, _ => false));
        Assert.Null(EarlierResults.After(RunOf("", 147), "run-one", 1, null, Saved));
    }

    // A comparison counts side A's targets: a three-target what-if after the full run shows 3 of 147.
    [Fact]
    public void AComparisonIsCountedBySideAsTargets()
    {
        var earlier = EarlierResults.After(Full, "run-side-a", 3, null, Saved);
        Assert.Equal(new EarlierResults("run-full", 147), earlier);
        // Side B of the same comparison becoming the shown run keeps it.
        Assert.Equal(earlier, EarlierResults.After(RunOf("run-side-a", 3), "run-side-b", 3, earlier, Saved));
    }
}
