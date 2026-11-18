using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SlotClockTests
{
    private static readonly PoolStats Counts = new(4, 1, 0, 0, 0, Resets: 3);

    private long _now;

    private SlotClock Clock(int slots) => new(slots, () => _now);

    private void At(double seconds) => _now = TimeSpan.FromSeconds(seconds).Ticks;

    private static long Ticks(double seconds) => TimeSpan.FromSeconds(seconds).Ticks;

    // The slot works for the given seconds on each kind, then leaves at the given time.
    private void Work(SlotClock clock, int slot, double leaveAt, params (SlotWork Work, double Seconds)[] parts)
    {
        SlotClock.Enter(slot);
        foreach (var (work, seconds) in parts)
        {
            clock.Add(work, Ticks(seconds));
            if (work == SlotWork.Fight)
                clock.CountFight();
        }
        At(leaveAt);
        clock.Leave();
    }

    [Fact]
    public void OneSlotIsIdleForWhatItDidNotTimeAndHasNoTail()
    {
        At(10);
        var clock = Clock(1);
        Work(
            clock,
            1,
            16,
            (SlotWork.Build, 1),
            (SlotWork.Prepare, 0.5),
            (SlotWork.Check, 0.25),
            (SlotWork.Fight, 2),
            (SlotWork.Reset, 0.25),
            (SlotWork.Fight, 1)
        );
        At(16);
        clock.End(Counts);

        Assert.Equal(2, clock.Fought);
        Assert.Equal(3, clock.Seconds(SlotWork.Fight), 6);
        Assert.Equal(1, clock.IdleSummed, 6); // 6 s of run, 5 s timed
        Assert.Equal(0, clock.Tail);
    }

    [Fact]
    public void FourSlotsSumTheirIdleAndTheTailStartsWhenTheSecondToLastLeaves()
    {
        At(0);
        var clock = Clock(4);
        Work(clock, 1, 6, (SlotWork.Fight, 6)); // busy until it leaves, then waits 4 s for the end
        Work(clock, 2, 7, (SlotWork.Fight, 5)); // 2 s idle before it leaves, 3 s after
        Work(clock, 3, 8, (SlotWork.Fight, 8)); // the second-to-last to leave
        Work(clock, 4, 10, (SlotWork.Fight, 9), (SlotWork.Reset, 1));
        At(10);
        clock.End(Counts);

        Assert.Equal(4, clock.Fought);
        Assert.Equal(4 + 5 + 2 + 0, clock.IdleSummed, 6);
        Assert.Equal(2, clock.Tail, 6);
    }

    [Fact]
    public void ASlotThatNeverGetsACopyIsIdleForTheWholeRun()
    {
        At(0);
        var clock = Clock(2);
        Work(clock, 1, 5, (SlotWork.Build, 1), (SlotWork.Fight, 4));
        At(5);
        clock.End(Counts); // slot 2 never left: it waited for memory until the run ended

        Assert.Equal(5, clock.IdleSummed, 6);
        Assert.Equal(0, clock.Tail, 6);
    }

    [Fact]
    public void WorkOnAThreadWithNoSlotCountsInTheTotalsAndInNoSlot()
    {
        At(0);
        var clock = Clock(1);
        SlotClock.Enter(0);
        clock.Add(SlotWork.Build, Ticks(3)); // a copy built ahead
        Work(clock, 1, 4, (SlotWork.Fight, 4));
        At(4);
        clock.End(Counts);

        Assert.Equal(3, clock.Seconds(SlotWork.Build), 6);
        Assert.Equal(0, clock.IdleSummed, 6);
    }

    [Fact]
    public void TimedWorkLongerThanTheRunLeavesNoNegativeIdle()
    {
        At(0);
        var clock = Clock(1);
        Work(clock, 1, 1, (SlotWork.Fight, 1.5));
        At(1);
        clock.End(Counts);

        Assert.Equal(0, clock.IdleSummed);
    }

    [Fact]
    public void EndingFillsThePoolsStatsAndKeepsItsCounts()
    {
        At(0);
        var clock = Clock(2);
        Work(clock, 1, 3, (SlotWork.Build, 1), (SlotWork.Prepare, 0.5), (SlotWork.Check, 0.25), (SlotWork.Fight, 1));
        Work(clock, 2, 4, (SlotWork.Fight, 3), (SlotWork.Reset, 0.5), (SlotWork.Fight, 0.5));
        At(4);
        var stats = clock.End(Counts);

        Assert.Equal((4, 1, 3), (stats.PeakCopies, stats.Built, stats.Resets));
        Assert.Equal((3, 2), (stats.Fought, stats.Slots));
        Assert.Equal(1, stats.BuildSeconds, 6);
        Assert.Equal(0.5, stats.PrepareSeconds, 6);
        Assert.Equal(0.25, stats.CheckSeconds, 6);
        Assert.Equal(4.5, stats.FightSeconds, 6);
        Assert.Equal(0.5, stats.ResetSeconds, 6);
        Assert.Equal(1.25, stats.IdleSeconds, 6);
        Assert.Equal(1, stats.TailSeconds, 6);
    }
}
