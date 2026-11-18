using MelvorMods.CombatSimulator2.Engine.Dev;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class BenchLimitsTests
{
    // 1000 frames of 16.7 ms with a few slow ones; p99 is the 990th.
    private static WindowStats Window(
        double fps = 60,
        double lagMax = 1,
        double lagEnd = 0.5,
        double gc = 200,
        params double[] slow
    )
    {
        var frames = Enumerable.Repeat(16.7, 1000 - slow.Length).Concat(slow).Order().ToList();
        var p99 = frames[(int)Math.Ceiling(0.99 * frames.Count) - 1];
        return new WindowStats(fps, 16.7, p99, frames[^1], lagMax, lagEnd, gc, frames.Count, 3, 1, 0, frames);
    }

    private static LimitResult Limit(IReadOnlyList<LimitResult> results, string name) =>
        Assert.Single(results, r => r.Name == name);

    [Fact]
    public void AnEqualRunPassesEveryLimit()
    {
        var control = Window(slow: [40, 45]);
        var results = BenchLimits.Evaluate(control, control);
        Assert.Equal(["fps", "tick-lag", "lost", "long", "gc-pause"], results.Select(r => r.Name));
        Assert.All(results, r => Assert.True(r.Pass, r.Name));
        Assert.True(BenchLimits.Pass(results));
    }

    [Fact]
    public void FpsMustStayWithin10Percent()
    {
        var control = Window(fps: 60);
        Assert.True(Limit(BenchLimits.Evaluate(control, Window(fps: 54)), "fps").Pass); // exactly at the bound
        var low = Limit(BenchLimits.Evaluate(control, Window(fps: 53.9)), "fps");
        Assert.False(low.Pass);
        Assert.Equal(53.9, low.Value);
        Assert.Equal(60, low.Control);
        Assert.Equal(54, low.Bound, 6);
    }

    [Fact]
    public void TickLagIsAtMostThreeOrControlPlusOne()
    {
        Assert.True(Limit(BenchLimits.Evaluate(Window(lagMax: 1), Window(lagMax: 3)), "tick-lag").Pass);
        Assert.False(Limit(BenchLimits.Evaluate(Window(lagMax: 1), Window(lagMax: 3.01)), "tick-lag").Pass);
        Assert.True(Limit(BenchLimits.Evaluate(Window(lagMax: 4), Window(lagMax: 5)), "tick-lag").Pass);
        Assert.False(Limit(BenchLimits.Evaluate(Window(lagMax: 4), Window(lagMax: 5.01)), "tick-lag").Pass);
    }

    [Fact]
    public void LostTicksCountOnlyLagBeyondTheControlsWorst()
    {
        var control = Window(lagMax: 1.5, lagEnd: 0.4);
        Assert.Equal(0, BenchLimits.Lost(Window(lagEnd: 1.3), control)); // within the control's own jitter
        Assert.Equal(0, BenchLimits.Lost(Window(lagEnd: 2.49), control));
        Assert.Equal(1, BenchLimits.Lost(Window(lagEnd: 2.5), control)); // exactly one tick past it
        Assert.Equal(55, BenchLimits.Lost(Window(lagEnd: 56.5), control));
        Assert.Equal(0, BenchLimits.Lost(control, control));
    }

    [Fact]
    public void LostTicksAreAtMostTheControls()
    {
        var control = Window(lagMax: 1.5, lagEnd: 0.4);
        Assert.True(Limit(BenchLimits.Evaluate(control, Window(lagMax: 1.3, lagEnd: 1.3)), "lost").Pass);
        Assert.False(Limit(BenchLimits.Evaluate(control, Window(lagMax: 2.9, lagEnd: 2.9)), "lost").Pass);
    }

    [Fact]
    public void LongFramesOverTwiceTheControlsP99AreAtMostTheControlsCountPlusOne()
    {
        // control p99 is 16.7 (fewer than 1% slow), so long means over 33.4 ms; the control has 2.
        var control = Window(slow: [40, 45]);
        var plusOne = Limit(BenchLimits.Evaluate(control, Window(slow: [40, 45, 50])), "long");
        Assert.True(plusOne.Pass);
        Assert.Equal(3, plusOne.Value);
        Assert.Equal(2, plusOne.Control);
        Assert.Equal(3, plusOne.Bound);
        Assert.False(Limit(BenchLimits.Evaluate(control, Window(slow: [40, 45, 50, 60])), "long").Pass);
        // exactly twice the control's p99 is not long
        Assert.True(Limit(BenchLimits.Evaluate(control, Window(slow: [33.4, 33.4, 33.4, 33.4])), "long").Pass);
    }

    [Fact]
    public void GcPauseIsWithinTheLargerOf10PercentAnd100MsOfTheControls()
    {
        Assert.True(Limit(BenchLimits.Evaluate(Window(gc: 200), Window(gc: 300)), "gc-pause").Pass);
        Assert.False(Limit(BenchLimits.Evaluate(Window(gc: 200), Window(gc: 300.1)), "gc-pause").Pass);
        Assert.True(Limit(BenchLimits.Evaluate(Window(gc: 2000), Window(gc: 2200)), "gc-pause").Pass);
        Assert.False(Limit(BenchLimits.Evaluate(Window(gc: 2000), Window(gc: 2200.1)), "gc-pause").Pass);
    }

    [Fact]
    public void OneFailedLimitFailsTheBench()
    {
        var results = BenchLimits.Evaluate(Window(), Window(fps: 30));
        Assert.False(BenchLimits.Pass(results));
        Assert.Single(results, r => !r.Pass);
    }
}
