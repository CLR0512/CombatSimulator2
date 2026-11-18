using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MetricsMasterTests
{
    internal static TargetResult Master(params TargetResult[] parts) =>
        new()
        {
            Key = "master:MelvorBase:Kuren",
            Name = "Kuren's tasks",
            Kind = TargetKind.Master,
            State = TargetState.Finished,
            Parts = [.. parts],
        };

    private static TargetResult Kuren()
    {
        var fast = MasterFiguresTests.Part("A/Fast", 10, 5, seed: 2);
        var slow = MasterFiguresTests.Part("A/Slow", 30, 20, seed: 3);
        foreach (var batch in slow.Batches)
            batch.Currency["MelvorBase:SlayerCoins"] = 40;
        return Master(fast, slow);
    }

    [Fact]
    public void AMasterOffersItsMonstersMetricsButNoDeathRate()
    {
        var keys = Metrics.For([Kuren()]).Select(m => m.Key).ToList();
        Assert.Contains("xp:MelvorBase:Slayer", keys);
        Assert.Contains("currency:MelvorBase:SlayerCoins", keys);
        Assert.Contains(Metrics.Kills, keys);
        Assert.DoesNotContain(Metrics.DeathRate, keys);
    }

    [Fact]
    public void BesideAMonsterTheDeathRateStays()
    {
        var keys = Metrics.For([Kuren(), MasterFiguresTests.Part("A/Other", 10, 1)]).Select(m => m.Key);
        Assert.Contains(Metrics.DeathRate, keys);
    }

    [Fact]
    public void AMastersEstimateIsItsTimeWeightedFigure()
    {
        var master = Kuren();
        var estimate = Metrics.Estimate(master, "xp:MelvorBase:Slayer", TimeUnit.Hour)!;
        Assert.Equal(MasterFigures.Estimate(master.Parts!, "xp:MelvorBase:Slayer", TimeUnit.Hour), estimate);
        Assert.NotNull(Metrics.Series(master, "currency:MelvorBase:SlayerCoins", TimeUnit.Hour));
        Assert.Null(Metrics.Estimate(master, Metrics.DeathRate, TimeUnit.Hour));
    }

    [Fact]
    public void AMasterNotFinishedHasNoFigures()
    {
        var master = Kuren();
        master.State = TargetState.Fighting;
        Assert.Null(Metrics.Estimate(master, Metrics.Kills, TimeUnit.Hour));
        Assert.Empty(Metrics.For([master]));
    }

    [Fact]
    public void ARunOfMastersWithoutItsMainSkillFindsItInTheirMonsters()
    {
        var master = Kuren();
        var run = new Run { Targets = [master.Key] };
        run.Results[master.Key] = master;
        Assert.Equal("xp:MelvorBase:Slayer", Metrics.Resolve(run, Metrics.MainXp));
    }

    [Fact]
    public void ARunOfMastersAloneTakesSlayerXpAsItsMainSkill()
    {
        var master = Kuren();
        var run = new Run { Targets = [master.Key], MainSkill = "MelvorBase:Ranged" };
        run.Results[master.Key] = master;
        Assert.Equal("xp:MelvorBase:Slayer", Metrics.Resolve(run, Metrics.MainXp));
    }

    [Fact]
    public void ARunOfMastersAndMonstersKeepsItsMainSkill()
    {
        var master = Kuren();
        var run = new Run { Targets = [master.Key, "A/Other"], MainSkill = "MelvorBase:Ranged" };
        run.Results[master.Key] = master;
        Assert.Equal("xp:MelvorBase:Ranged", Metrics.Resolve(run, Metrics.MainXp));
    }

    [Fact]
    public void AMastersFightNumbersComeFromItsMonsters()
    {
        var master = Kuren();
        foreach (var batch in master.Parts!.SelectMany(p => p.Batches))
            batch.Fight = FightCountsTests.Sample();
        Assert.True(master.HasFightCounts);
        var hit = FightNumbers.Estimate(master, FightNumbers.Hit);
        Assert.NotNull(hit);
        Assert.Equal(Metrics.Estimate(master, FightNumbers.Hit, TimeUnit.Hour), hit);
        Assert.Null(FightNumbers.Estimate(master, FightNumbers.RanOver));
    }
}
