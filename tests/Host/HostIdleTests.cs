using MelvorMods.CombatSimulator2.Host;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HostIdleTests
{
    private DateTime _now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private HostIdle Idle() => new(() => _now);

    private void Wait(double seconds) => _now = _now.AddSeconds(seconds);

    [Fact]
    public void WithThePanelOpenTheHostStaysFifteenMinutesAfterItsLastRun()
    {
        var idle = Idle();
        idle.Panel(open: true);
        var since = _now;
        Wait(899);
        Assert.Null(idle.Passed(since));
        Wait(2);
        Assert.Equal(TimeSpan.FromMinutes(15), idle.Passed(since));
    }

    [Fact]
    public void WithThePanelClosedItGoesTwoMinutesAfterTheClose()
    {
        var idle = Idle();
        idle.Panel(open: true);
        var since = _now;
        Wait(600);
        idle.Panel(open: false);
        Wait(119);
        Assert.Null(idle.Passed(since));
        Wait(2);
        Assert.Equal(TimeSpan.FromMinutes(2), idle.Passed(since));
    }

    [Fact]
    public void ARunGoingAtTheCloseFinishesAndThenGetsTheWholeLimit()
    {
        var idle = Idle();
        idle.Panel(open: true);
        idle.Panel(open: false);
        Wait(300);
        // Busy all that time: nothing to count from, so it stays however long ago the panel closed.
        Assert.Null(idle.Passed(null));
        var ended = _now;
        Wait(119);
        Assert.Null(idle.Passed(ended));
        Wait(2);
        Assert.NotNull(idle.Passed(ended));
    }

    [Fact]
    public void AHostStartedWithThePanelNeverOpenedCountsFromItsStart()
    {
        var idle = Idle();
        var started = _now;
        Wait(119);
        Assert.Null(idle.Passed(started));
        Wait(2);
        Assert.NotNull(idle.Passed(started));
    }

    [Fact]
    public void ReopeningThePanelGivesTheLongLimitBack()
    {
        var idle = Idle();
        idle.Panel(open: true);
        var since = _now;
        idle.Panel(open: false);
        Wait(100);
        idle.Panel(open: true);
        Wait(100);
        Assert.Null(idle.Passed(since));
        idle.Panel(open: false);
        Wait(119);
        Assert.Null(idle.Passed(since)); // the close that counts is the last one
        Wait(2);
        Assert.NotNull(idle.Passed(since));
    }

    [Fact]
    public void TheLimitsCanBeSetAndOpeningTwiceDoesNotMoveTheClose()
    {
        var idle = Idle();
        idle.ClosedLimit = TimeSpan.FromSeconds(20);
        idle.OpenLimit = TimeSpan.FromSeconds(60);
        idle.Panel(open: true);
        idle.Panel(open: false);
        var since = _now;
        Wait(15);
        idle.Panel(open: false); // told again while closed: still counted from the first
        Wait(6);
        Assert.Equal(TimeSpan.FromSeconds(20), idle.Passed(since));
    }
}
