using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class DeathLimitsTests
{
    private static TargetResult Deaths(int deaths, int trials = 300) =>
        Set(1, 10, deaths: deaths, trials: trials).Result;

    [Fact]
    public void NoDeathsDropsAtTheFirstDeath()
    {
        Assert.False(DeathLimits.Over(DeathLimit.None, Deaths(0)));
        Assert.True(DeathLimits.Over(DeathLimit.None, Deaths(1)));
        Assert.True(DeathLimits.Within(DeathLimit.None, Deaths(0)));
        Assert.False(DeathLimits.Within(DeathLimit.None, Deaths(1)));
    }

    [Fact]
    public void UnderFivePercentDropsOnlyWhenTheWholeRangeIsOver()
    {
        // 6% of 300: the range still reaches below 5%, so the set stays in the race.
        Assert.False(DeathLimits.Over(DeathLimit.UnderFivePercent, Deaths(18)));
        Assert.True(Estimators.Wilson(30, 300).Low >= 0.05);
        Assert.True(DeathLimits.Over(DeathLimit.UnderFivePercent, Deaths(30)));
    }

    [Fact]
    public void UnderFivePercentKeepsOnlySharesUnderFiveAtTheEnd()
    {
        Assert.True(DeathLimits.Within(DeathLimit.UnderFivePercent, Deaths(14)));
        Assert.False(DeathLimits.Within(DeathLimit.UnderFivePercent, Deaths(15)));
        Assert.False(DeathLimits.Within(DeathLimit.UnderFivePercent, Deaths(18)));
    }

    [Fact]
    public void SurvivedOnceDropsOnlyASetThatDiedEveryTrial()
    {
        Assert.False(DeathLimits.Over(DeathLimit.SurvivedOnce, Deaths(299)));
        Assert.True(DeathLimits.Over(DeathLimit.SurvivedOnce, Deaths(300)));
        Assert.True(DeathLimits.Within(DeathLimit.SurvivedOnce, Deaths(299)));
        Assert.False(DeathLimits.Within(DeathLimit.SurvivedOnce, Deaths(300)));
    }

    [Fact]
    public void AResultWithoutTrialsIsNeverWithin()
    {
        var none = Without(1, TargetState.Refused).Result;
        foreach (var limit in Enum.GetValues<DeathLimit>())
        {
            Assert.False(DeathLimits.Within(limit, none));
            Assert.False(DeathLimits.Over(limit, none));
        }
    }

    [Fact]
    public void TheClosestHasTheSmallestShareOfDeaths()
    {
        Assert.Equal(
            2,
            DeathLimits.Closest([
                (1, Deaths(30)),
                (2, Deaths(6)),
                (3, Deaths(6)),
                (4, Without(4, TargetState.Refused).Result),
            ])
        );
        Assert.Equal(5, DeathLimits.Closest([(4, Deaths(10, 300)), (5, Deaths(10, 600))]));
        Assert.Null(DeathLimits.Closest([]));
    }
}
