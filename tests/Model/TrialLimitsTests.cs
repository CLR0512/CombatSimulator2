using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class TrialLimitsTests
{
    [Fact]
    public void AMastersMonstersGetAnHourAndTheirOwnRowsKeepFiveMinutes()
    {
        var limits = new TrialLimits();
        Assert.Equal(3600, limits.For(TargetKind.Enemy, part: true));
        Assert.Equal(300, limits.For(TargetKind.Enemy, part: false));
        Assert.Equal(1800, limits.For(TargetKind.Dungeon, part: false));
        Assert.Equal(limits.For(TargetKind.Enemy), limits.For(TargetKind.Enemy, part: false));
    }
}
