using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PerfHistoryTests
{
    private static PerfHistory History(params double[] cpu)
    {
        var history = new PerfHistory("run-1", "host");
        for (var i = 0; i < cpu.Length; i++)
            history.Add(new PerfSample(i, cpu[i], (i + 1) * 100L, 1000L - i, 144 - i));
        return history;
    }

    [Fact]
    public void StatsGiveNowAverageLowAndPeak()
    {
        var stats = History(10, 30, 20).Stats(PerfMetric.Cpu)!;
        Assert.Equal(20, stats.Now);
        Assert.Equal(20, stats.Average, 9);
        Assert.Equal(10, stats.Low);
        Assert.Equal(30, stats.Peak);
    }

    [Fact]
    public void EachMetricReadsItsOwnSeries()
    {
        var history = History(10, 30, 20);
        Assert.Equal(300, history.Stats(PerfMetric.Ram)!.Peak);
        Assert.Equal(998, history.Stats(PerfMetric.Free)!.Low);
        Assert.Equal(142, history.Stats(PerfMetric.Fps)!.Now);
        Assert.Equal([10, 30, 20], history.Series(PerfMetric.Cpu));
    }

    [Fact]
    public void AnEmptyHistoryHasNoStats() => Assert.Null(new PerfHistory("r", "game").Stats(PerfMetric.Cpu));

    [Fact]
    public void AVeryLongRunKeepsItsWholeSpanAtLowerResolution()
    {
        var history = new PerfHistory("r", "host");
        for (var i = 0; i < PerfHistory.MaxSamples * 3; i++)
            history.Add(new PerfSample(i, i, 0, 0, 0));
        Assert.True(history.Samples.Count <= PerfHistory.MaxSamples);
        Assert.Equal(0, history.Samples[0].Seconds);
        Assert.True(history.Samples[^1].Seconds >= PerfHistory.MaxSamples * 3 - 8); // within one stride of the end
        Assert.Equal(PerfHistory.MaxSamples * 3 - 1, history.Stats(PerfMetric.Cpu)!.Now);
    }
}
