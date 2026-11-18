using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class GoalTests
{
    [Theory]
    [InlineData("kills")]
    [InlineData("xp:main")]
    [InlineData("used:food")]
    [InlineData("item:MelvorBase:Bones")]
    public void TheFightCountsForEveryMetric(string metric)
    {
        Assert.True(new Goal(metric).Counts(PartGroup.Fight, targetAttacks: false));
        Assert.True(new Goal(metric).Counts(PartGroup.Other, targetAttacks: false));
    }

    [Theory]
    [InlineData("kills")]
    [InlineData("xp:MelvorBase:Attack")]
    public void DefenceCountsOnlyWhenTheTargetAttacks(string metric)
    {
        Assert.True(new Goal(metric).Counts(PartGroup.Defence, targetAttacks: true));
        Assert.False(new Goal(metric).Counts(PartGroup.Defence, targetAttacks: false));
    }

    [Theory]
    [InlineData("xp:main", false)]
    [InlineData("item:MelvorBase:Bones", true)]
    [InlineData("currency:MelvorBase:GP", true)]
    [InlineData("value:MelvorBase:GP", true)]
    [InlineData("worth:MelvorBase:GP", true)]
    [InlineData("bury:MelvorBase:PrayerPoints", true)]
    [InlineData("rare:MelvorBase:Pet", true)]
    [InlineData("drops", true)]
    [InlineData("kills", false)]
    [InlineData("killTime", false)]
    [InlineData("deaths", false)]
    [InlineData("used:food", false)]
    public void RewardsCountForWhatAKillDrops(string metric, bool counts) =>
        Assert.Equal(counts, new Goal(metric).Counts(PartGroup.Rewards, targetAttacks: true));

    [Theory]
    [InlineData("xp:main", true)]
    [InlineData("xp:MelvorBase:Attack", true)]
    [InlineData("kills", false)]
    [InlineData("drops", false)]
    public void XpGainsCountOnlyForXp(string metric, bool counts) =>
        Assert.Equal(counts, new Goal(metric).Counts(PartGroup.Xp, targetAttacks: true));

    [Theory]
    [InlineData("used:food", true)]
    [InlineData("used:ammo:MelvorBase:IronArrows", true)]
    [InlineData("kills", false)]
    [InlineData("xp:main", false)]
    public void CostsCountForWhatIsUsed(string metric, bool counts) =>
        Assert.Equal(counts, new Goal(metric).Counts(PartGroup.Costs, targetAttacks: true));

    [Fact]
    public void TheDeathLimitDefaultsToNoDeaths() => Assert.Equal(DeathLimit.None, new Goal("kills").Deaths);

    [Theory]
    [InlineData("kills")]
    [InlineData("killTime")]
    [InlineData("xp:main")]
    [InlineData("xp:MelvorBase:Strength")]
    [InlineData("drops")]
    [InlineData("item:MelvorBase:Bones")]
    [InlineData("currency:MelvorBase:GoldPieces")]
    [InlineData("value:MelvorBase:GoldPieces")]
    [InlineData("worth:MelvorBase:GoldPieces")]
    [InlineData("bury:MelvorBase:PrayerPoints")]
    [InlineData("rare:MelvorBase:Pet")]
    public void MetricsTheGamesDirectionHoldsForAllowComparing(string metric) =>
        Assert.True(Goal.AllowsComparing(metric));

    [Theory]
    [InlineData("deaths")]
    [InlineData("deathRate")]
    [InlineData("highestHit")]
    [InlineData("lowestHp")]
    [InlineData("highestReflect")]
    [InlineData("used:food")]
    [InlineData("used:ammo:MelvorBase:IronArrows")]
    [InlineData("fight:hit")]
    [InlineData("fight:dps")]
    [InlineData("fight:taken")]
    [InlineData("something-new")]
    public void EveryOtherMetricFightsEverySet(string metric) => Assert.False(Goal.AllowsComparing(metric));
}
