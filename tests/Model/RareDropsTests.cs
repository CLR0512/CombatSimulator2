using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RareDropsTests
{
    private const string Coat = "MelvorBase:RainbowSlimeCoat";
    private const string Signet = "MelvorBase:SignetEvent";

    // 30 batches of 10 trials, 100 game seconds each (one kill every 10 s), with the coat gained once every
    // third batch and, when asked, the event seen in batch 0.
    private static TargetResult Result(List<DropChance>? drops, TargetKind kind = TargetKind.Enemy, bool seen = false)
    {
        var result = new TargetResult
        {
            Key = "MelvorBase:SlimeForest/MelvorBase:RainbowSlime",
            Kind = kind,
            State = TargetState.Finished,
            TrialsDone = 300,
            Drops = drops,
        };
        for (var b = 0; b < 30; b++)
        {
            var tally = new Tally
            {
                Trials = 10,
                Kills = 10,
                GameSeconds = 100,
            };
            if (b % 3 == 0)
                tally.Gained[Coat] = 1;
            if (seen && b == 0)
                tally.RareEvents[Signet] = 1;
            result.Batches.Add(tally);
        }
        return result;
    }

    private static DropChance Loot(string id, double chance, DropSource source = DropSource.Loot) =>
        new()
        {
            Source = source,
            Id = id,
            Chance = chance,
            Per = source == DropSource.Reward ? ChancePer.Completion : ChancePer.Kill,
        };

    [Fact]
    public void OnlyEntriesBelowOnePercentAreRare()
    {
        Assert.Equal(0.01, RareDrops.Threshold);
        var rows = RareDrops.Rows(Result([Loot(Coat, 0.0024), Loot("MelvorBase:Bones", 0.01)]), TimeUnit.Hour);
        Assert.Equal([Coat], rows.Select(r => r.Id));
    }

    [Fact]
    public void ALootRowGivesTheGamesChanceTheExpectedRateAndWhatWasSeen()
    {
        var result = Result([Loot(Coat, 0.0024)]);
        var row = Assert.Single(RareDrops.Rows(result, TimeUnit.Hour));
        Assert.Equal(DropSource.Loot, row.Source);
        Assert.Equal(0.0024, row.Chance);
        Assert.Equal(ChancePer.Kill, row.Per);
        var kills = Metrics.Estimate(result, Metrics.Kills, TimeUnit.Hour)!;
        Assert.Equal(360, kills.Point, 9);
        Assert.Equal(kills.Point * 0.0024, row.Expected!.Point, 9);
        Assert.Equal(kills.Low * 0.0024, row.Expected.Low, 9);
        Assert.Equal(kills.High * 0.0024, row.Expected.High, 9);
        Assert.Equal(10, row.Observed);
        Assert.True(row.Possible);
        Assert.False(row.ObservedOnly);
    }

    [Fact]
    public void PerKillTheExpectedRateIsTheChance()
    {
        var row = Assert.Single(RareDrops.Rows(Result([Loot(Coat, 0.0024)]), TimeUnit.Kill));
        Assert.Equal(0.0024, row.Expected!.Point, 12);
    }

    [Fact]
    public void ADungeonsRewardIsPerCompletion()
    {
        var result = Result([Loot(Coat, 0.005, DropSource.Reward)], TargetKind.Dungeon);
        var row = Assert.Single(RareDrops.Rows(result, TimeUnit.Hour));
        Assert.Equal(DropSource.Reward, row.Source);
        Assert.Equal(ChancePer.Completion, row.Per);
        Assert.Equal(360 * 0.005, row.Expected!.Point, 9);
    }

    [Fact]
    public void ARareEventGivesItsChancePerMinuteAndTheObservedRate()
    {
        var drop = new DropChance
        {
            Source = DropSource.RareEvent,
            Id = Signet,
            Chance = 0.001,
            Per = ChancePer.MinuteInCombat,
        };
        var result = Result([drop], seen: true);
        var row = Assert.Single(RareDrops.Rows(result, TimeUnit.Hour));
        Assert.Equal(DropSource.RareEvent, row.Source);
        Assert.Equal(0.001, row.Chance);
        Assert.Equal(ChancePer.MinuteInCombat, row.Per);
        Assert.Null(row.Expected); // CS2 doesn't turn the game's timer into a rate
        Assert.Equal(1, row.Observed);
        Assert.Equal(Metrics.Estimate(result, "rare:" + Signet, TimeUnit.Hour), row.ObservedRate);
    }

    [Fact]
    public void ARareEventWithUnmetRequirementsIsNotPossible()
    {
        var drop = new DropChance
        {
            Source = DropSource.RareEvent,
            Id = Signet,
            Chance = 0.001,
            Per = ChancePer.MinuteInCombat,
            Unmet = [Note.Text("Requires Slayer level 50")],
        };
        var row = Assert.Single(RareDrops.Rows(Result([drop]), TimeUnit.Hour));
        Assert.False(row.Possible);
        Assert.Equal("Requires Slayer level 50", Assert.Single(row.Unmet).English());
    }

    [Fact]
    public void AnEventSeenButNotListedIsShownAsObserved()
    {
        var rows = RareDrops.Rows(Result([Loot(Coat, 0.0024)], seen: true), TimeUnit.Hour);
        Assert.Equal(2, rows.Count);
        var seen = rows.Single(r => r.Id == Signet);
        Assert.True(seen.ObservedOnly);
        Assert.Null(seen.Chance);
        Assert.Equal(1, seen.Observed);
        Assert.NotNull(seen.ObservedRate);
    }

    [Fact]
    public void AnOlderRunHasNoRows()
    {
        Assert.Empty(RareDrops.Rows(Result(null), TimeUnit.Hour));
        var unfinished = Result([Loot(Coat, 0.0024)]);
        unfinished.State = TargetState.Unfinished;
        Assert.Empty(RareDrops.Rows(unfinished, TimeUnit.Hour));
    }

    private static DropChance Counted(string id, double chance, double? quantity) =>
        new()
        {
            Source = DropSource.Loot,
            Id = id,
            Chance = chance,
            Per = ChancePer.Kill,
            Quantity = quantity,
        };

    [Fact]
    public void ARareItemsRateIsTheKillsTimesWhatEachKillGivesOnAverage()
    {
        // 360 kills an hour; 10 coats seen in 3000 s would be 12 an hour.
        var result = Result([Counted(Coat, 0.0024, 1.5)]);
        var rate = Metrics.Estimate(result, "item:" + Coat, TimeUnit.Hour)!;
        Assert.Equal(360 * 0.0024 * 1.5, rate.Point, 9);
        var series = Metrics.Series(result, "item:" + Coat, TimeUnit.Hour)!.Value;
        Assert.All(series.Values, v => Assert.Equal(10 * 0.0024 * 1.5, v, 12));
    }

    [Fact]
    public void EveryRareEntryOfTheItemAddsUp()
    {
        var result = Result([Counted(Coat, 0.002, 1), Counted(Coat, 0.001, 2)]);
        Assert.Equal(0.004, RareDrops.ExpectedPerKill(result, Coat)!.Value, 12);
        Assert.Equal(360 * 0.004, Metrics.Estimate(result, "item:" + Coat, TimeUnit.Hour)!.Point, 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithoutAQuantityTheRateIsWhatTheRunCounted(bool partly)
    {
        // An older run, or an item the target also drops some other way.
        List<DropChance> drops = partly ? [Counted(Coat, 0.002, 1), Counted(Coat, 0.001, null)] : [Loot(Coat, 0.0024)];
        var result = Result(drops);
        Assert.Null(RareDrops.ExpectedPerKill(result, Coat));
        Assert.Equal(12, Metrics.Estimate(result, "item:" + Coat, TimeUnit.Hour)!.Point, 9);
    }

    [Fact]
    public void AnItemNotListedIsWhatTheRunCounted()
    {
        var result = Result([Counted("MelvorBase:Bones", 0.002, 1)]);
        Assert.Null(RareDrops.ExpectedPerKill(result, Coat));
        Assert.Equal(12, Metrics.Estimate(result, "item:" + Coat, TimeUnit.Hour)!.Point, 9);
    }

    [Fact]
    public void TheFirstDropTakesTheAverageKillsOfItsChanceAndTheirTime()
    {
        // One kill every 10 s; 0.25% a kill: 400 kills, 4,000 s, on average. A drop of 2 changes neither.
        var one = Assert.Single(RareDrops.Rows(Result([Counted(Coat, 0.0025, 2)]), TimeUnit.Hour)).ForOne!;
        Assert.Equal(ChancePer.Kill, one.Per);
        Assert.Equal(400, one.Each!.Value, 9);
        Assert.Equal(4000, one.Time!.Point, 6);
        Assert.True(one.Time.Low <= 4000 && one.Time.High >= 4000);
    }

    [Fact]
    public void TheFirstOneDependsOnTheChanceAloneNotOnWhatADropGives()
    {
        var one = Assert.Single(RareDrops.Rows(Result([Counted(Coat, 0.0025, 2)]), TimeUnit.Hour)).ForOne!;
        Assert.Equal(Math.Log(0.5) / Math.Log(1 - 0.0025), one.Half, 9);
        Assert.Equal(Math.Log(0.1) / Math.Log(1 - 0.0025), one.NineInTen, 9);
        Assert.Equal(one.Half * 10, one.HalfTime!.Value, 6);
    }

    [Fact]
    public void AnOlderRunWithoutQuantitiesStillHasItsFirstDrop()
    {
        var one = Assert.Single(RareDrops.Rows(Result([Loot(Coat, 0.0024)]), TimeUnit.Hour)).ForOne!;
        Assert.Equal(1 / 0.0024, one.Each!.Value, 9);
    }

    [Fact]
    public void ARareEventsOneIsInMinutesInCombat()
    {
        var drop = new DropChance
        {
            Source = DropSource.RareEvent,
            Id = Signet,
            Chance = 0.001,
            Per = ChancePer.MinuteInCombat,
        };
        var one = RareDrops.Rows(Result([drop]), TimeUnit.Hour).Single(r => r.Id == Signet).ForOne!;
        Assert.Null(one.Each);
        Assert.Equal(1000 * 60, one.Time!.Point, 6);
        Assert.Equal(Math.Log(0.5) / Math.Log(1 - 0.001), one.Half, 9);
    }

    [Fact]
    public void AnItemsTimeForOneCoversEveryEntryAndNeedsTheirQuantities()
    {
        var both = Result([Counted(Coat, 0.002, 2), Counted(Coat, 0.003, 1)]);
        Assert.Equal(200 * 10, ForOnes.Time(both, Coat)!.Point, 6);
        Assert.Null(ForOnes.Time(Result([Loot(Coat, 0.0024)]), Coat));
    }

    [Fact]
    public void ARareItemNeverDroppedStillHasItsRateWhenItsChancesCoverIt()
    {
        const string Map = "MelvorBase:MapToAshenLands";
        var covered = Result([Counted(Map, 0.0012, 1)]);
        Assert.Contains(Metrics.For([covered]), m => m.Key == "item:" + Map);
        Assert.Equal(360 * 0.0012, Metrics.Estimate(covered, "item:" + Map, TimeUnit.Hour)!.Point, 9);
        Assert.DoesNotContain(Metrics.For([Result([Counted(Map, 0.0012, null)])]), m => m.Key == "item:" + Map);
    }
}
