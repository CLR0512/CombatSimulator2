using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunTimingsTextTests
{
    private static RunTimings Full() =>
        new()
        {
            FirstResult = 0.05,
            Total = 7.5,
            Snapshot = 0.0015,
            HostWait = 0.01,
            Check = 0.02,
            Builds = 1,
            FromStock = 3,
            BuildsSummed = 1.1,
            PrepareSummed = 0.2,
            Resets = 50,
            ResetMisses = 1,
            ResetSummed = 2.5,
            FightSummed = 24,
            Report = 0.015,
            Reused = 0,
            Fought = 50,
            Slots = 4,
            IdleSummed = 1.25,
            Tail = 0.4,
        };

    [Fact]
    public void AFullRunKeepsTheOldFieldsInOrderAndEndsWithTheNewOnes()
    {
        Assert.Equal(
            "timings first 0.05 total 7.50 snapshot 1.5 wait 0.01 check 0.02 builds 1 (3 stock) 1.10 prepare 0.20"
                + " resets 50 (1 missed) 2.50 fight 24.00 report 15 reused 0 fought 50 idle 1.25 tail 0.40"
                + " per-target 150 overhead 25%",
            RunTimingsText.Line(Full())
        );
    }

    [Fact]
    public void OverheadIsClockTimeOverFightTimeSharedBySlots()
    {
        var t = Full();
        Assert.Equal(150, RunTimingsText.PerTargetMs(t)!.Value, 6);
        Assert.Equal(7.5 / (24.0 / 4) - 1, RunTimingsText.Overhead(t)!.Value, 6);
    }

    [Fact]
    public void ARunSavedBeforeSlotsWereCountedShowsZerosAndNoDerivedFigures()
    {
        var t = Full();
        t.Fought = 0;
        t.Slots = 0;
        t.IdleSummed = 0;
        t.Tail = 0;
        var line = RunTimingsText.Line(t);
        Assert.EndsWith(" reused 0 fought 0 idle 0.00 tail 0.00", line);
        Assert.Null(RunTimingsText.Overhead(t));
    }

    [Fact]
    public void ARunThatFoughtNothingHasNoDerivedFigures()
    {
        var t = new RunTimings
        {
            Total = 0.02,
            Reused = 50,
            Slots = 4,
        };
        Assert.EndsWith(" reused 50 fought 0 idle 0.00 tail 0.00", RunTimingsText.Line(t));
        Assert.Null(RunTimingsText.PerTargetMs(t));
    }
}
