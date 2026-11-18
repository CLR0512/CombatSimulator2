using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MotionLevelTests
{
    private static IEnumerable<MotionKind> Ending => MotionTimes.Kinds.Where(k => !MotionTimes.Repeats(k));

    [Theory]
    [InlineData(MotionKind.Fade, 0.10, 0.08)]
    [InlineData(MotionKind.Fill, 0.15, 0)]
    [InlineData(MotionKind.Count, 0.15, 0)]
    [InlineData(MotionKind.Strip, 0.10, 0)]
    [InlineData(MotionKind.Sheen, 3.2, 0)]
    [InlineData(MotionKind.Pulse, 1.0, 0)]
    [InlineData(MotionKind.Breathe, 1.4, 0)]
    public void EachKindHasItsLengthPerLevel(MotionKind kind, double full, double reduced)
    {
        Assert.Equal(full, MotionTimes.Seconds(kind, MotionLevel.Full));
        Assert.Equal(reduced, MotionTimes.Seconds(kind, MotionLevel.Reduced));
        Assert.Equal(0, MotionTimes.Seconds(kind, MotionLevel.Off));
    }

    [Fact]
    public void NothingThatEndsIsLongerThanTheLongestAtFull() =>
        Assert.All(
            Ending,
            kind => Assert.InRange(MotionTimes.Seconds(kind, MotionLevel.Full), 0.01, MotionTimes.Longest)
        );

    // A change that is waited on never got longer. A repeating motion is not waited on: a longer round is a calmer one.
    [Fact]
    public void NothingIsLongerAtFullThanItWasBefore() =>
        Assert.All(
            MotionTimes.Kinds.Where(k => MotionTimes.Before(k) != null && !MotionTimes.Repeats(k)),
            kind => Assert.True(MotionTimes.Seconds(kind, MotionLevel.Full) <= MotionTimes.Before(kind))
        );

    [Fact]
    public void ReducedKeepsOnlyShortFades() =>
        Assert.All(
            MotionTimes.Kinds,
            kind =>
                Assert.Equal(
                    kind == MotionKind.Fade ? MotionTimes.LongestReduced : 0,
                    MotionTimes.Seconds(kind, MotionLevel.Reduced)
                )
        );

    [Fact]
    public void ALevelNeverTakesLongerThanTheOneAboveIt() =>
        Assert.All(
            MotionTimes.Kinds,
            kind =>
            {
                Assert.True(
                    MotionTimes.Seconds(kind, MotionLevel.Reduced) <= MotionTimes.Seconds(kind, MotionLevel.Full)
                );
                Assert.True(
                    MotionTimes.Seconds(kind, MotionLevel.Off) <= MotionTimes.Seconds(kind, MotionLevel.Reduced)
                );
            }
        );

    [Theory]
    [InlineData("full", MotionLevel.Full)]
    [InlineData("reduced", MotionLevel.Reduced)]
    [InlineData("off", MotionLevel.Off)]
    public void ALevelReadsBackFromItsName(string name, MotionLevel level)
    {
        Assert.Equal(name, MotionTimes.Name(level));
        Assert.True(MotionTimes.TryParse(name, out var read));
        Assert.Equal(level, read);
    }

    [Fact]
    public void AnUnknownNameIsNoLevel() => Assert.False(MotionTimes.TryParse("slow", out _));
}
