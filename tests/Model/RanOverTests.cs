using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RanOverTests
{
    internal static Stall OneTrial(long hpLeft, long hpMax, int? stage = null, string? enemy = null) =>
        new()
        {
            Trials = 1,
            Counts = FightCountsTests.Sample(),
            HpLeft = hpLeft,
            HpMax = hpMax,
            WardLeft = 3,
            LimitSeconds = 300,
            Stage = stage,
            Enemy = enemy,
        };

    [Fact]
    public void AddingATrialSumsItsNumbersAndKeepsTheFirstStage()
    {
        var stall = OneTrial(10, 100, 2, "a");
        var second = OneTrial(30, 100, 3, "b");
        second.Immune = true;
        stall.Add(second);
        Assert.Equal(2, stall.Trials);
        Assert.Equal(200, stall.Counts.Dealt.Attacks);
        Assert.Equal(40, stall.HpLeft);
        Assert.Equal(200, stall.HpMax);
        Assert.Equal(6, stall.WardLeft);
        Assert.True(stall.Immune);
        Assert.Equal(2, stall.Stage);
        Assert.Equal("a", stall.Enemy);
    }

    [Fact]
    public void TheHealthShareIsOverEveryTrial()
    {
        var stall = OneTrial(10, 100);
        stall.Add(OneTrial(50, 300));
        Assert.Equal(0.15, stall.HpLeftShare, 10);
        Assert.Equal(0, new Stall().HpLeftShare);
    }

    [Fact]
    public void AStallRoundTripsWithoutItsDerivedShare()
    {
        var result = new TargetResult
        {
            Key = "a/b",
            State = TargetState.Unfinished,
            Stall = OneTrial(10, 100, 1, "x"),
        };
        var json = JsonSerializer.Serialize(result, RunFile.Options);
        Assert.Contains("\"stall\"", json, StringComparison.Ordinal);
        Assert.Contains("\"hpLeft\"", json, StringComparison.Ordinal);
        Assert.Contains("\"limitSeconds\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hpLeftShare", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ranOver\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<TargetResult>(json, RunFile.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, RunFile.Options));
    }

    [Fact]
    public void AMonsterStallLeavesStageAndEnemyOut()
    {
        var json = JsonSerializer.Serialize(OneTrial(1, 2), RunFile.Options);
        Assert.DoesNotContain("\"stage\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"enemy\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOldResultHasNoStallAndNothingRanOver()
    {
        foreach (var result in RunFile.Read(RunFileFormatTests.Format2Run).Results.Values)
        {
            Assert.Null(result.Stall);
            Assert.Equal(0, result.RanOver);
        }
    }

    [Fact]
    public void RanOverAddsTheBatches()
    {
        var result = new TargetResult();
        result.Batches.Add(new Tally { RanOver = 2 });
        result.Batches.Add(new Tally());
        result.Batches.Add(new Tally { RanOver = 1 });
        Assert.Equal(3, result.RanOver);
    }
}
