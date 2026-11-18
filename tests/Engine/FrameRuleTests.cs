using MelvorMods.CombatSimulator2.Engine.Dev;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class FrameRuleTests
{
    // 1000 frames of 6.9 ms (144 fps) with a few longer ones.
    private static WindowStats Window(params double[] longer)
    {
        var frames = Enumerable.Repeat(6.9, 1000 - longer.Length).Concat(longer).Order().ToList();
        return new WindowStats(144, 6.9, frames[989], frames[^1], 1, 0, 0, frames.Count, 0, 0, 0, frames);
    }

    [Fact]
    public void AWindowEqualToItsControlPasses()
    {
        var control = Window(14.5, 30);
        var verdict = FrameRule.Judge(control, control);
        Assert.True(verdict.Pass);
        Assert.Equal((30, 2, 30, 2), (verdict.Worst, verdict.Slow, verdict.ControlWorst, verdict.ControlSlow));
    }

    [Fact]
    public void AFrameWorseThanTheControlsWorstFails()
    {
        var control = Window(30);
        Assert.True(FrameRule.Judge(control, Window(30)).Pass); // exactly at the bound
        Assert.False(FrameRule.Judge(control, Window(30.1)).Pass);
    }

    [Fact]
    public void SlowFramesAreThoseOverTwiceTheControlsMedian()
    {
        var control = Window(20, 30);
        // 13.8 ms is exactly twice the median: not slow. 13.9 is.
        Assert.Equal(2, FrameRule.Judge(control, Window(13.8, 20, 30)).Slow);
        Assert.Equal(3, FrameRule.Judge(control, Window(13.9, 20, 30)).Slow);
    }

    [Fact]
    public void MoreSlowFramesThanTheControlFailsEvenUnderItsWorst()
    {
        var control = Window(20, 30);
        var verdict = FrameRule.Judge(control, Window(15, 16, 17));
        Assert.Equal((17, 3), (verdict.Worst, verdict.Slow));
        Assert.False(verdict.Pass);
    }

    [Fact]
    public void TheWindowsOwnMedianDoesNotMoveTheBar()
    {
        // A window that is slower throughout is judged by the control's median, so all its frames are slow.
        var control = Window(40);
        var frames = Enumerable.Repeat(15.0, 1000).ToList();
        var slower = new WindowStats(66, 15, 15, 15, 1, 0, 0, 1000, 0, 0, 0, frames);
        Assert.Equal(1000, FrameRule.Judge(control, slower).Slow);
    }

    private static FrameVerdict Take(bool pass) => new(0, 0, 0, 0, pass);

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, true)] // one collection in one take doesn't fail a budget
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    public void ABudgetFailsOnlyWhenTwoOfThreeTakesFail(bool a, bool b, bool c, bool pass) =>
        Assert.Equal(pass, FrameRule.Pass([Take(a), Take(b), Take(c)]));

    [Fact]
    public void OneTakeIsItsOwnVerdictAndNoneIsNotAPass()
    {
        Assert.True(FrameRule.Pass([Take(true)]));
        Assert.False(FrameRule.Pass([Take(false)]));
        Assert.False(FrameRule.Pass([]));
    }

    [Fact]
    public void ControlsWhoseWorstFramesDifferByMoreThanTwiceAreNoisy()
    {
        Assert.False(FrameRule.Noisy([Window(15), Window(20), Window(30)])); // exactly twice
        Assert.True(FrameRule.Noisy([Window(15), Window(20), Window(30.1)]));
        Assert.False(FrameRule.Noisy([Window(90)]));
    }
}
