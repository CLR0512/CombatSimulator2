using MelvorMods.CombatSimulator2.Engine;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunPeakTests
{
    [Fact]
    public void IdleReadsZeroAndIgnoresReadings()
    {
        var peak = new RunPeak();
        peak.Read(900);
        Assert.Equal(0, peak.Bytes);
        Assert.Equal(0, peak.End(900));
    }

    [Fact]
    public void ARunKeepsItsHighestReading()
    {
        var peak = new RunPeak();
        peak.Start(500);
        peak.Read(800);
        peak.Read(700);
        Assert.Equal(800, peak.Bytes);
        Assert.Equal(800, peak.End(600));
        Assert.Equal(0, peak.Bytes);
    }

    [Fact]
    public void ARunShorterThanTheIntervalHasItsStartAndEnd()
    {
        var peak = new RunPeak();
        peak.Start(500);
        Assert.Equal(650, peak.End(650));
    }

    [Fact]
    public void EachRunStartsAgainSoALowerRunReportsItsOwnPeak()
    {
        var peak = new RunPeak();
        peak.Start(500);
        peak.Read(3000);
        peak.End(2500);
        peak.Start(900);
        peak.Read(1200);
        Assert.Equal(1200, peak.End(1000));
    }

    [Fact]
    public void AnEndWithNoRunLeavesTheNextRunAlone()
    {
        var peak = new RunPeak();
        peak.Start(500);
        peak.End(700);
        Assert.Equal(0, peak.End(9000));
        peak.Start(400);
        Assert.Equal(400, peak.Bytes);
    }
}
