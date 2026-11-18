using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ProcessMeterTests
{
    [Theory]
    [InlineData(1.0, 1.0, 4, 25)] // one core busy out of four
    [InlineData(4.0, 1.0, 4, 100)]
    [InlineData(0.5, 2.0, 8, 3.125)]
    [InlineData(9.0, 1.0, 4, 100)] // clamped: timer noise can overshoot
    [InlineData(1.0, 0.0, 4, 0)] // no time passed yet
    [InlineData(-1.0, 1.0, 4, 0)]
    public void CpuIsPercentOfTheWholeMachine(double cpuSeconds, double wallSeconds, int cores, double percent) =>
        Assert.Equal(percent, ProcessMeter.Percent(cpuSeconds, wallSeconds, cores), 9);

    [Theory]
    [InlineData(500L * 1024 * 1024, "500 MB")]
    [InlineData(1536L * 1024 * 1024, "1.5 GB")]
    [InlineData(10L * 1024 * 1024 * 1024, "10.0 GB")]
    [InlineData(0L, "0 MB")]
    public void MemoryReadsInMbOrGb(long bytes, string text) => Assert.Equal(text, Sizes.Bytes(bytes));
}
