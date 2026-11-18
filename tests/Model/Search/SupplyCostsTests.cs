using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SupplyCostsTests
{
    private const string Rune = "MelvorBase:AirRune";
    private const string Trout = "MelvorBase:Trout";

    // A finished result whose every batch is 60 s of fighting and used what is given, per batch.
    internal static TargetResult Using(params (string Key, long Each)[] used)
    {
        var result = RankingTests.Finished("t", "t", 10);
        foreach (var batch in result.Batches)
        foreach (var (key, each) in used)
            batch.Used[key] = each;
        return result;
    }

    private static bool IsFood(string id) => id == Trout;

    private static IReadOnlyList<SupplyCost> Costs(params (string Key, double PerHour)[] costs) =>
        [.. costs.Select(c => new SupplyCost(c.Key, new Estimate(c.PerHour, c.PerHour, c.PerHour, 1, "ratio")))];

    [Fact]
    public void EachSupplyIsInItsOwnUnitsAnHourAndTotalsAreLeftOut()
    {
        var result = Using(
            ("item:" + Rune, 2),
            ("runes", 2),
            ("currency:" + Metrics.PrayerPoints, 6),
            ("ammo:MelvorBase:BronzeArrows", 1),
            ("ammo", 1),
            ("charges:MelvorBase:BoneCasing", 1),
            ("consumables", 1)
        );
        var costs = SupplyCosts.Of(result, IsFood);
        Assert.Equal(
            [
                "used:currency:" + Metrics.PrayerPoints,
                "used:item:" + Rune,
                "used:ammo:MelvorBase:BronzeArrows",
                "used:charges:MelvorBase:BoneCasing",
            ],
            costs.Select(c => c.Key)
        );
        // 6 a minute of fighting.
        Assert.Equal(360, costs[0].PerHour.Point, 6);
    }

    [Fact]
    public void FoodIsNotASupply()
    {
        var result = Using(("item:" + Trout, 3), ("food", 3));
        Assert.Empty(SupplyCosts.Of(result, IsFood));
        Assert.Equal(180, SupplyCosts.Food(result)!.Point, 6);
    }

    [Fact]
    public void NothingUsedIsNoSupply() => Assert.Empty(SupplyCosts.Of(Using(), IsFood));

    [Fact]
    public void FewerSuppliesIsNoMoreOfAnyAndLessOfOne()
    {
        var a = Costs(("used:a", 10), ("used:b", 5));
        Assert.True(SupplyCosts.FewerSupplies(Costs(("used:a", 10)), a));
        Assert.True(SupplyCosts.FewerSupplies(Costs(("used:a", 9), ("used:b", 5)), a));
        Assert.False(SupplyCosts.FewerSupplies(a, a));
        Assert.False(SupplyCosts.FewerSupplies(a, Costs(("used:a", 10))));
    }

    [Fact]
    public void TradingOneSupplyForAnotherIsNeitherFewer()
    {
        var arrows = Costs(("used:ammo", 300), ("used:prayer", 1000));
        var prayer = Costs(("used:ammo", 200), ("used:prayer", 1400));
        Assert.False(SupplyCosts.FewerSupplies(arrows, prayer));
        Assert.False(SupplyCosts.FewerSupplies(prayer, arrows));
    }

    [Fact]
    public void UsingNothingIsFewerThanUsingSomething() =>
        Assert.True(SupplyCosts.FewerSupplies([], Costs(("used:a", 1))));

    [Fact]
    public void ASetsSuppliesAreTheMeanOfEachTargetsOwnRate()
    {
        var a = Using(("currency:" + Metrics.PrayerPoints, 6));
        var b = Using(("currency:" + Metrics.PrayerPoints, 2));
        var set = new TargetResult
        {
            Key = "area:A",
            Kind = TargetKind.Set,
            State = TargetState.Finished,
            Parts = [a, b],
            // Weights that would make it a gain, which a supply isn't.
            Weights = [0.001, 0.002],
        };
        var cost = Assert.Single(SupplyCosts.Of(set, IsFood));
        Assert.Equal((360 + 120) / 2.0, cost.PerHour.Point, 6);
    }
}
