using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class LogScopeTests
{
    private const string Run = "run-20261109-140209-1";

    [Fact]
    public void NoScopeWritesNoContextAndNoBar()
    {
        Assert.Equal("", LogLine.Context(default));
        Assert.Equal("started", LogLine.WithContext("started"));
    }

    [Fact]
    public void EveryKeyIsWrittenInItsOrder()
    {
        var scope = new LogScope(Run, 'B', RunPhase.Fight, "melvorD:Wolf", 2);
        Assert.Equal($"run={Run} side=B phase=fight target=melvorD:Wolf copy=2 | ", LogLine.Context(scope));
    }

    [Theory]
    [InlineData(true, false, false, false, false, "run=R | ")]
    [InlineData(false, true, false, false, false, "side=A | ")]
    [InlineData(false, false, true, false, false, "phase=build | ")]
    [InlineData(false, false, false, true, false, "target=T | ")]
    [InlineData(false, false, false, false, true, "copy=1 | ")]
    [InlineData(true, false, true, false, true, "run=R phase=build copy=1 | ")]
    [InlineData(false, true, false, true, false, "side=A target=T | ")]
    public void UnsetKeysAreLeftOut(bool run, bool side, bool phase, bool target, bool copy, string expected)
    {
        var scope = new LogScope(
            run ? "R" : null,
            side ? 'A' : null,
            phase ? RunPhase.Build : null,
            target ? "T" : null,
            copy ? 1 : null
        );
        Assert.Equal(expected, LogLine.Context(scope));
    }

    [Theory]
    [InlineData(0, "plan")]
    [InlineData(1, "build")]
    [InlineData(2, "fight")]
    [InlineData(3, "report")]
    public void PhasesAreWrittenInLowerCase(int phase, string name) =>
        Assert.Equal($"phase={name} | ", LogLine.Context(new LogScope(Phase: (RunPhase)phase)));

    [Fact]
    public void APushKeepsTheOuterKeysAndDisposingRestoresThem()
    {
        using (LogScope.Push(run: Run, side: 'A'))
        {
            using (LogScope.Push(phase: RunPhase.Fight, target: "T", copy: 3))
                Assert.Equal($"run={Run} side=A phase=fight target=T copy=3 | x", LogLine.WithContext("x"));
            Assert.Equal($"run={Run} side=A | x", LogLine.WithContext("x"));
            using (LogScope.Push(phase: RunPhase.Report))
                Assert.Equal(RunPhase.Report, LogScope.Current.Phase);
        }
        Assert.Equal(default, LogScope.Current);
    }

    [Fact]
    public void AnInnerPushOverridesTheSameKey()
    {
        using var outer = LogScope.Push(target: "first");
        using (LogScope.Push(target: "second"))
            Assert.Equal("second", LogScope.Current.Target);
        Assert.Equal("first", LogScope.Current.Target);
    }

    [Fact]
    public void AScopeStaysOnItsThreadUntilAnotherThreadEntersIt()
    {
        using var scope = LogScope.Push(run: Run);
        LogScope seen = new("unset");
        LogScope entered = default;
        var outer = LogScope.Current;
        var thread = new Thread(() =>
        {
            seen = LogScope.Current;
            using (LogScope.Enter(outer))
                entered = LogScope.Current;
        });
        thread.Start();
        thread.Join();

        Assert.Equal(default, seen);
        Assert.Equal(Run, entered.Run);
        Assert.Equal(Run, LogScope.Current.Run);
    }
}
