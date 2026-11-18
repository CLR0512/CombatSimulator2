using MelvorMods.CombatSimulator2.Engine.Search;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SearchTimesTests
{
    // Targets of their own, so the session-wide record other tests may add to doesn't sway them.
    private static string Target(string name) => "SearchTimesTests:" + name + Guid.NewGuid().ToString("N");

    [Fact]
    public void ATargetFoughtBeforeIsTimedOnItsOwnFights()
    {
        var slow = Target("Slow");
        var quick = Target("Quick");
        SearchTimes.Record(slow, TargetKind.Dungeon, 10, 100, 100, whole: true);
        SearchTimes.Record(quick, TargetKind.Dungeon, 10, 100, 1, whole: true);
        Assert.Equal(0.1, SearchTimes.PerTrial(slow, TargetKind.Dungeon)!.Value, 6);
        Assert.Equal(0.001, SearchTimes.PerTrial(quick, TargetKind.Dungeon)!.Value, 6);
    }

    [Fact]
    public void ATargetNotFoughtYetIsTimedAsItsKind()
    {
        var fought = Target("Fought");
        SearchTimes.Record(fought, TargetKind.Enemy, 10, 100, 5, whole: true);
        var kind = SearchTimes.PerTrial(Target("New"), TargetKind.Enemy);
        Assert.NotNull(kind);
        Assert.True(kind > 0);
    }

    [Fact]
    public void StepsCountThreeTimesOverForAWholeSearch()
    {
        var target = Target("Steps");
        SearchTimes.Record(target, TargetKind.Enemy, 10, 100, 1, whole: false);
        Assert.Equal(0.001 * SearchTimes.WholeOverSteps, SearchTimes.PerTrial(target, TargetKind.Enemy)!.Value, 6);
    }

    // 60 sets of 1,000 trials: stages of 100, 400 and 1,000, a sixth of the sets reaching 400 and a twentieth 1,000.
    // Staged, a set at the end has fought all three; fought once, each set costs the stage it leaves at.
    [Fact]
    public void ARaceFoughtOnceCostsTheStageEachSetLeavesAt()
    {
        Assert.Equal(60 * (100 + 400 / 6.0 + 1000 / 20.0), SearchTimes.Expected(1, 60, 1000), 6);
        Assert.Equal(
            60 * (100 * (5 / 6.0) + 400 * (1 / 6.0 - 1 / 20.0) + 1000 / 20.0),
            SearchTimes.Expected(1, 60, 1000, foughtOnce: true),
            6
        );
    }
}
