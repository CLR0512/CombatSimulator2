using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class TaskVerdictsTests
{
    private const string SlayerXp = "xp:MelvorBase:Slayer";

    private static TargetResult Part(string name, double seconds) =>
        MasterFiguresTests.Part(Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:" + name), seconds, 100, seed: 3);

    // Three monsters at 100 XP a kill: Cow fast, Jack average, Slug slow.
    private static TargetResult Master() =>
        new()
        {
            Key = "master:MelvorBase:Kuren",
            Kind = TargetKind.Master,
            State = TargetState.Finished,
            Parts = [Part("Cow", 5), Part("Jack", 20), Part("Slug", 60)],
            Blocked = [],
        };

    private static TaskNow Task(string enemy, int progress = 10, bool extended = false) =>
        new(
            "MelvorBase:Kuren",
            "MelvorBase:" + enemy,
            progress,
            100,
            extended,
            50,
            500,
            1000,
            "MelvorBase:SlayerCoins"
        );

    [Fact]
    public void AFastTaskIsWorthExtendingByTheBaseLength()
    {
        var verdict = TaskVerdicts.For(Task("Cow"), Master(), SlayerXp)!;
        Assert.Equal(TaskAdvice.Extend, verdict.Advice);
        Assert.Equal(50, verdict.Kills);
        Assert.Equal(1000, verdict.Cost);
        Assert.True(verdict.Ratio > 1);
        // 50 more Cow kills take 250 s; at the master's average those 250 s give less XP than the 5,000 Cows give.
        var expected = (verdict.Rate.Point - verdict.Average.Point) * 50 * 5 / 3600;
        Assert.Equal(expected, verdict.Gain!.Value, 0.05 * expected);
        Assert.Equal(50 * 5 / 3600.0, verdict.Hours, 0.1 * 50 * 5 / 3600.0);
    }

    [Fact]
    public void AnExtendedTaskIsKeptHoweverGood()
    {
        Assert.Equal(TaskAdvice.Keep, TaskVerdicts.For(Task("Cow", extended: true), Master(), SlayerXp)!.Advice);
    }

    [Fact]
    public void ASlowTaskIsWorthRerollingOverTheKillsLeft()
    {
        var verdict = TaskVerdicts.For(Task("Slug", progress: 40), Master(), SlayerXp)!;
        Assert.Equal(TaskAdvice.Reroll, verdict.Advice);
        Assert.Equal(60, verdict.Kills);
        Assert.Equal(500, verdict.Cost);
        Assert.True(verdict.Gain > 0);
    }

    [Fact]
    public void AFinishedSlowTaskHasNothingToReroll()
    {
        Assert.Equal(TaskAdvice.Keep, TaskVerdicts.For(Task("Slug", progress: 100), Master(), SlayerXp)!.Advice);
    }

    [Fact]
    public void ByTheCoinPaidAnActionMustEarnMoreThanItCosts()
    {
        // 1 coin a kill: rerolling 60 Slug kills gains a few coins, far under the 500 it costs.
        var master = Master();
        foreach (var part in master.Parts!)
        foreach (var batch in part.Batches)
            batch.Currency["MelvorBase:SlayerCoins"] = batch.Kills;
        var verdict = TaskVerdicts.For(Task("Slug", progress: 40), master, "currency:MelvorBase:SlayerCoins")!;
        Assert.Equal(TaskAdvice.Keep, verdict.Advice);
        Assert.True(verdict.SameCoin);
    }

    [Fact]
    public void ATaskWithinFivePercentOfItsMasterIsKept()
    {
        // Jack and a near twin: their master's rate is within a few percent of Jack's.
        var master = new TargetResult
        {
            Key = "master:MelvorBase:Kuren",
            Kind = TargetKind.Master,
            State = TargetState.Finished,
            Parts = [Part("Jack", 20), Part("Twin", 21)],
            Blocked = [],
        };
        Assert.Equal(TaskAdvice.Keep, TaskVerdicts.For(Task("Jack"), master, SlayerXp)!.Advice);
    }

    [Fact]
    public void ATaskOnABlockedMonsterIsStillJudged()
    {
        var master = Master();
        master.Blocked = ["MelvorBase:Farm/MelvorBase:Slug"];
        var verdict = TaskVerdicts.For(Task("Slug", progress: 40), master, SlayerXp)!;
        Assert.Equal(TaskAdvice.Reroll, verdict.Advice);
    }

    [Fact]
    public void AMonsterOutsideTheRunHasNoVerdict()
    {
        Assert.Null(TaskVerdicts.For(Task("Wolf"), Master(), SlayerXp));
    }
}
