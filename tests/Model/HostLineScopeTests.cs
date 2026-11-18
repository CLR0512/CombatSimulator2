using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HostLineScopeTests
{
    private const string Run = "run-20261113-224919-1";

    private static string L(string context, string text) => $"22:49:28.677 [CombatSimulator2/host] {context} | {text}";

    [Fact]
    public void TheTargetsStillFightingAtTheEnd()
    {
        string[] lines =
        [
            L($"run={Run} phase=fight target=a:1 copy=1", "fight started"),
            L($"run={Run} phase=fight target=a:2 copy=2", "fight started"),
            L($"run={Run} phase=fight target=a:1 copy=1", "fought run=x slot=1"),
            L($"run={Run} phase=fight target=a:3 copy=1", "fight started"),
        ];

        var (phase, inFlight) = HostLineScope.Of(lines, Run);

        Assert.Equal("fight", phase);
        Assert.Equal(["a:2", "a:3"], inFlight);
    }

    [Fact]
    public void ALeverBeforeAnyFightLeavesNoPhase()
    {
        string[] lines =
        [
            L("run=run-old phase=fight target=a:1 copy=1", "fight started"),
            L("run=run-new", "run 4: 147 targets, seed 1"),
            "22:49:39.571 [CombatSimulator2/host] WARN dev: failing fast",
        ];

        Assert.Equal((null, Array.Empty<string>()), HostLineScope.Of(lines, "run-new"));
    }

    [Fact]
    public void TheBuildPhase()
    {
        string[] lines = [L($"run={Run} phase=build copy=3", "building")];

        var (phase, inFlight) = HostLineScope.Of(lines, null);

        Assert.Equal("build", phase);
        Assert.Empty(inFlight);
    }

    [Fact]
    public void WarningsKeepTheirContext()
    {
        string[] lines = [$"22:49:28.677 [CombatSimulator2/host] WARN run={Run} phase=fight target=a:1 | slow"];

        Assert.Equal(["a:1"], HostLineScope.Of(lines, Run).InFlight);
    }
}
