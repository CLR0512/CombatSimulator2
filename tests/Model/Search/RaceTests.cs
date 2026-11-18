using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RaceTests
{
    private static readonly Goal Kills = new(Metrics.Kills);
    private const double OnePercent = 0.01;

    [Fact]
    public void AClearLoserIsDroppedAtTheFirstStage()
    {
        var step = Race.After([Set(0, 12), Set(1, 10), Set(2, 20)], Kills, OnePercent);
        Assert.Equal(1, step.Leader);
        Assert.Equal(DropReason.Behind, step.Dropped[2]);
        Assert.Equal([0, 1], step.In);
        Assert.Equal(0, step.Behind[1]);
        Assert.Equal(0.5, step.Behind[2], 2);
    }

    [Fact]
    public void CurrentGearIsNeverDroppedBeforeTheEnd()
    {
        var step = Race.After([Set(0, 30), Set(1, 10)], Kills, OnePercent);
        Assert.Empty(step.Dropped);
        Assert.Equal([0, 1], step.In);
        var dying = Race.After([Set(0, 10, deaths: 5), Set(1, 12)], Kills, OnePercent);
        Assert.Equal(1, dying.Leader);
        Assert.Equal([0, 1], dying.In);
    }

    [Fact]
    public void ASetInsideTheShareIsNeverDropped()
    {
        // Half a percent behind, moving with the leader: its whole range is behind, yet inside the margin.
        StageResult[] sets = [Set(0, 12), Set(1, 10), Set(2, 10.05)];
        var (delta, _) = Race.Difference(sets[1], sets[2]);
        Assert.True(delta!.High < 0);
        Assert.DoesNotContain(2, Race.After(sets, Kills, OnePercent).Dropped.Keys);
    }

    [Fact]
    public void AtShareZeroTheRangeAloneDrops()
    {
        StageResult[] sets = [Set(0, 12), Set(1, 10), Set(2, 10.05), Set(3, 10.02, Own)];
        var step = Race.After(sets, Kills, 0);
        Assert.Equal(DropReason.Behind, step.Dropped[2]);
        // A wobble of its own makes the range wide enough to include the leader.
        Assert.DoesNotContain(3, step.Dropped.Keys);
    }

    [Fact]
    public void ASetOverTheDeathLimitOrWithoutAResultIsDropped()
    {
        var step = Race.After(
            [
                Set(0, 12),
                Set(1, 10),
                Set(2, 8, deaths: 1),
                Without(3, TargetState.Refused),
                Without(4, TargetState.Unfinished),
            ],
            Kills,
            OnePercent
        );
        Assert.Equal(1, step.Leader);
        Assert.Equal(DropReason.OverLimit, step.Dropped[2]);
        Assert.Equal(DropReason.Refused, step.Dropped[3]);
        Assert.Equal(DropReason.Unfinished, step.Dropped[4]);
        var any = Race.After(
            [Set(0, 12), Set(2, 8, deaths: 1)],
            new Goal(Metrics.Kills, DeathLimit.SurvivedOnce),
            OnePercent
        );
        Assert.Equal(2, any.Leader);
    }

    [Fact]
    public void TheBestIsFoundWithItsGainOverCurrentGear()
    {
        var end = Race.End([Set(0, 12), Set(1, 10)], Kills, OnePercent);
        Assert.Equal(AnswerState.Found, end.State);
        Assert.Equal(1, end.Best);
        Assert.Empty(end.Ties);
        Assert.Equal(360 - 300, end.Gain!.Point, 0);
        Assert.True(end.Gain.Low > 0);
        Assert.Equal(0.2, end.Percent!.Point, 2);
        Assert.Equal("paired", end.Gain.Method);
    }

    [Fact]
    public void ATieByRange()
    {
        var end = Race.End([Set(0, 12), Set(1, 10), Set(2, 10.02, Own)], Kills, 0);
        Assert.Equal(1, end.Best);
        var tie = Assert.Single(end.Ties);
        Assert.Equal(2, tie.Index);
        Assert.True(tie.FromBest!.Low < 0 && tie.FromBest.High > 0);
    }

    [Fact]
    public void ATieByShareWhoseRangeExcludesZero()
    {
        StageResult[] sets = [Set(0, 12), Set(1, 10), Set(2, 10.05)];
        Assert.Equal([2], Race.End(sets, Kills, OnePercent).Ties.Select(t => t.Index));
        Assert.Empty(Race.End(sets, Kills, 0).Ties);
    }

    [Fact]
    public void CurrentGearAmongTheTiesIsKept()
    {
        var end = Race.End([Set(0, 10.05), Set(1, 10), Set(2, 10.02)], Kills, OnePercent);
        Assert.Equal(AnswerState.KeepCurrent, end.State);
        Assert.Equal(0, end.Best);
        Assert.Equal([1, 2], end.Ties.Select(t => t.Index));
        Assert.Null(end.Gain);
        // Each tie's difference is from current gear, the set shown.
        Assert.True(end.Ties[0].FromBest!.Point > 0);
    }

    [Fact]
    public void CurrentGearAsTheBestIsKept()
    {
        var end = Race.End([Set(0, 10), Set(1, 12)], Kills, OnePercent);
        Assert.Equal(AnswerState.KeepCurrent, end.State);
        Assert.Empty(end.Ties);
    }

    [Fact]
    public void ASetOverTheLimitIsNeitherBestNorTie()
    {
        var end = Race.End([Set(0, 12), Set(1, 10), Set(2, 8, deaths: 1), Set(3, 10, deaths: 1)], Kills, OnePercent);
        Assert.Equal(1, end.Best);
        Assert.Empty(end.Ties);
    }

    [Fact]
    public void WithNoSetWithinTheLimitTheClosestIsNamed()
    {
        var end = Race.End([Set(0, 12, deaths: 40), Set(1, 10, deaths: 3), Set(2, 9, deaths: 9)], Kills, OnePercent);
        Assert.Equal(AnswerState.NoneWithinLimit, end.State);
        Assert.Null(end.Best);
        Assert.Equal(1, end.Closest);
    }

    [Fact]
    public void ASetThatDiedEveryTrialIsNeverTheAnswer()
    {
        var goal = new Goal(Metrics.MainXp, DeathLimit.SurvivedOnce);
        var end = Race.End(
            [
                Set(0, 12, deaths: 300, metric: "xp:MelvorBase:Attack"),
                Set(1, 9, deaths: 300, metric: "xp:MelvorBase:Attack"),
            ],
            goal,
            OnePercent
        );
        Assert.Equal(AnswerState.NoneWithinLimit, end.State);
    }

    [Fact]
    public void WithNoSetFinishedNoSetKills()
    {
        var end = Race.End([Without(0, TargetState.Unfinished), Without(1, TargetState.Unfinished)], Kills, OnePercent);
        Assert.Equal(AnswerState.NoneKills, end.State);
    }

    [Fact]
    public void LowerIsBetterForTimePerKill()
    {
        var goal = new Goal(Metrics.KillTime);
        StageResult[] sets =
        [
            Set(0, 12, metric: Metrics.KillTime),
            Set(1, 10, metric: Metrics.KillTime),
            Set(2, 20, metric: Metrics.KillTime),
        ];
        var step = Race.After(sets, goal, OnePercent);
        Assert.Equal(1, step.Leader);
        Assert.Equal(DropReason.Behind, step.Dropped[2]);
        var end = Race.End([sets[0], sets[1]], goal, OnePercent);
        Assert.Equal(1, end.Best);
        Assert.True(end.Gain!.Point < 0);
    }

    [Fact]
    public void XpInTheMainSkillComparesEachSetsOwnSkill()
    {
        var a = Set(0, 12, metric: "xp:MelvorBase:Attack");
        var b = Set(1, 10, metric: "xp:MelvorBase:Strength");
        b.Result.Batches.ForEach(t => t.Xp = new(StringComparer.Ordinal) { ["MelvorBase:Strength"] = t.Kills * 40.0 });
        var end = Race.End([a, b], new Goal(Metrics.MainXp), OnePercent);
        Assert.Equal(1, end.Best);
        Assert.True(end.Gain!.Low > 0);
    }

    [Fact]
    public void ABestThatCameFromBehindIsFoundByItsStage()
    {
        var first = Race.After([Set(0, 12), Set(1, 10), Set(2, 10.3, Own)], Kills, OnePercent);
        var second = Race.After([Set(0, 12), Set(1, 10), Set(2, 9.99, Own)], Kills, OnePercent);
        Assert.Equal(1, Race.BehindAt([first, second], 2, OnePercent));
        Assert.Null(Race.BehindAt([first, second], 1, OnePercent));
        Assert.Null(Race.BehindAt([second], 2, OnePercent));
        // A short first stage left out, the stage is still counted from the first.
        Assert.Equal(2, Race.BehindAt([second, first], 2, OnePercent, from: 1));
        Assert.Null(Race.BehindAt([first], 2, OnePercent, from: 1));
    }

    [Fact]
    public void TheRunnerUpIsTheBestAfterTheBestInsideTheDeathLimit()
    {
        Assert.Equal(0, Race.RunnerUp([Set(0, 12), Set(1, 10), Set(2, 20)], 1, Kills, 0.5, _ => true));
        Assert.Equal(2, Race.RunnerUp([Set(0, 11, deaths: 5), Set(1, 10), Set(2, 20)], 1, Kills, 0.6, _ => true));
        Assert.Null(Race.RunnerUp([Set(1, 10)], 1, Kills, 0.5, _ => true));
        // Too far behind to be worth a climb of its own.
        Assert.Null(Race.RunnerUp([Set(0, 20), Set(1, 10)], 1, Kills, 0.1, _ => true));
        // Only the sets allowed: here, not the first.
        Assert.Equal(2, Race.RunnerUp([Set(0, 12), Set(1, 10), Set(2, 13)], 1, Kills, 0.5, i => i != 0));
    }

    [Fact]
    public void OfSetsAsGoodTheSafestIsTheAnswer()
    {
        // Equal on kills: the set that takes less damage is the answer over current gear.
        var end = Race.End([Taking(Set(0, 10), 25), Taking(Set(1, 10), 20), Taking(Set(2, 10), 22)], Kills, OnePercent);
        Assert.Equal(AnswerState.Found, end.State);
        Assert.Equal(1, end.Best);
        Assert.Equal([0, 2], end.Ties.Select(t => t.Index));
    }

    [Fact]
    public void CurrentGearStaysWhenItIsAboutAsSafe()
    {
        var end = Race.End([Taking(Set(0, 10), 20.1), Taking(Set(1, 10), 20)], Kills, OnePercent);
        Assert.Equal(AnswerState.KeepCurrent, end.State);
    }

    [Fact]
    public void AClearlyBetterSetStillWinsOverASaferOne()
    {
        var end = Race.End([Taking(Set(0, 12), 5), Taking(Set(1, 10), 30)], Kills, OnePercent);
        Assert.Equal(AnswerState.Found, end.State);
        Assert.Equal(1, end.Best);
        Assert.Empty(end.Ties);
    }

    [Fact]
    public void DamageNearNothingNeverDecides()
    {
        // The enemy barely hits: the set ahead on kills, within the margin, takes less damage, but inside the noise. Current gear stays.
        var end = Race.End(
            [Taking(Set(0, 10), 0.010, noise: 1), Taking(Set(1, 10), 0.009, noise: 1)],
            Kills,
            OnePercent
        );
        Assert.Equal(AnswerState.KeepCurrent, end.State);
    }
}
