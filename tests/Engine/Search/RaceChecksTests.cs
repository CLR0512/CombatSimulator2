using MelvorMods.CombatSimulator2.Engine.Search;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RaceChecksTests
{
    private static readonly Goal Kills = new(Metrics.Kills);

    private static RaceChecks Race(TargetResult held)
    {
        var race = new RaceChecks([100, 400, 1000], held: 0);
        race.Rule([held, held], Metrics.Kills, Kills, 0.01, false, null, PriceTable.Empty);
        return race;
    }

    private static TargetResult At(double secondsPerKill, int trials) => Set(1, secondsPerKill, trials: trials).Result;

    [Fact]
    public void ARoundChecksAtItsStagesBelowTheLast()
    {
        var race = new RaceChecks([25, 100, 400, 1000], held: 3);
        Assert.Equal([25, 100, 400], race.At);
        Assert.Equal(1000, race.Trials);
        Assert.Equal(3, race.Held);
    }

    [Fact]
    public void OnlyOneTargetsStepsAndDroptimizerAreFoughtOnce()
    {
        Assert.True(RaceChecks.Apply(TargetKind.Enemy, true, false, 3));
        Assert.True(RaceChecks.Apply(TargetKind.Dungeon, true, false, 3));
        Assert.False(RaceChecks.Apply(TargetKind.Enemy, false, false, 3));
        Assert.False(RaceChecks.Apply(TargetKind.Enemy, true, true, 3));
        Assert.False(RaceChecks.Apply(TargetKind.Enemy, true, false, 1));
        Assert.False(RaceChecks.Apply(TargetKind.Master, true, false, 3));
        Assert.False(RaceChecks.Apply(TargetKind.Set, true, false, 3));
    }

    [Fact]
    public void ASetFoughtToTheEndKeepsEachCheckAndItsResultAtTheTrials()
    {
        var race = Race(At(12, 300));
        var full = At(10, 1000);
        full.AtChecks = [At(10, 100), At(10, 400)];
        var (read, result, stays) = race.Took(1, full, Metrics.Kills);
        Assert.True(stays);
        Assert.Same(full, result);
        Assert.Null(full.AtChecks);
        Assert.Equal([100, 400, 1000], read.Select(r => r.Count));
        Assert.Equal(1000, race.Fought);
    }

    [Fact]
    public void ASetStoppedAtACheckEndsWithItsResultThere()
    {
        var race = Race(At(12, 300));
        var at100 = At(20, 100);
        var fought = new TargetResult { State = TargetState.Fighting, AtChecks = [at100] };
        var (read, result, stays) = race.Took(1, fought, Metrics.Kills);
        Assert.False(stays);
        Assert.Same(at100, result);
        Assert.Equal([100], read.Select(r => r.Count));
        Assert.Equal(100, race.Fought);
        Assert.Equal([DropReason.Behind], race.Reasons);
    }

    [Fact]
    public void EachCheckIsSummarisedAsAStage()
    {
        var race = Race(At(12, 300));
        race.Took(1, new TargetResult { State = TargetState.Fighting, AtChecks = [At(20, 100)] }, Metrics.Kills);
        race.Took(
            2,
            new TargetResult { State = TargetState.Fighting, AtChecks = [At(11, 100), At(30, 400)] },
            Metrics.Kills
        );
        var full = At(11, 1000);
        full.AtChecks = [At(11, 100), At(11, 400)];
        race.Took(3, full, Metrics.Kills);
        // Five sets came in, the held set and one answered from earlier among them.
        Assert.Equal([(100, 5, 4), (400, 4, 3)], race.Summaries(5));
        Assert.Equal(100 + 400 + 1000, race.Fought);
    }

    [Fact]
    public void ARefusedSetCostsNoTrials()
    {
        var race = Race(At(12, 300));
        var (_, _, stays) = race.Took(
            1,
            new TargetResult { State = TargetState.Refused, Reasons = [Note.Text("no")] },
            Metrics.Kills
        );
        Assert.True(stays);
        Assert.Equal(0, race.Fought);
    }
}
