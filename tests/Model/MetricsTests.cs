using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MetricsTests
{
    private static readonly PriceTable Prices = new(
        new Dictionary<string, Price>
        {
            ["MelvorBase:Bones"] = new("MelvorBase:GP", 1),
            ["MelvorBase:Gem"] = new("MelvorBase:GP", 50),
            ["MelvorBase:Token"] = new("MelvorBase:SlayerCoins", 3),
        }
    );

    private static TargetResult Result(Action<Tally, int> fill, int batches = 30)
    {
        var result = new TargetResult
        {
            Key = "a/b",
            State = TargetState.Finished,
            TrialsDone = batches * 10,
        };
        for (var b = 0; b < batches; b++)
        {
            var tally = new Tally
            {
                Trials = 10,
                Kills = 10,
                GameSeconds = 60 + b % 5,
            };
            fill(tally, b);
            result.Batches.Add(tally);
        }
        return result;
    }

    [Fact]
    public void ANewItemIdBecomesAMetric()
    {
        var result = Result((t, _) => t.Gained["SomeMod:BrandNewThing"] = 2);
        var keys = Metrics.For([result]).Select(m => m.Key).ToList();
        Assert.Contains("item:SomeMod:BrandNewThing", keys);
        var e = Metrics.Estimate(result, "item:SomeMod:BrandNewThing", TimeUnit.Kill, Prices);
        Assert.NotNull(e);
        Assert.Equal(0.2, e.Point, 9);
    }

    [Fact]
    public void TheListHoldsExactlyWhatIsPresent()
    {
        var result = Result(
            (t, b) =>
            {
                t.Xp["MelvorBase:Attack"] = 40;
                t.Used["ammo:MelvorBase:Arrow"] = 3;
                if (b == 4)
                    t.RareEvents["MelvorBase:Shooting"] = 1;
            }
        );
        var keys = Metrics.For([result]).Select(m => m.Key).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "kills",
                "deaths",
                "deathRate",
                "killTime",
                "xp:MelvorBase:Attack",
                "used:ammo:MelvorBase:Arrow",
                "rare:MelvorBase:Shooting",
            },
            keys
        );
    }

    [Fact]
    public void TotalValueAddsTheCurrencyDroppedToTheItemsSoldForIt()
    {
        var result = Result(
            (t, _) =>
            {
                t.Gained["MelvorBase:Gem"] = 2;
                t.Currency["MelvorBase:GP"] = 30;
            }
        );
        var keys = Metrics.For([result], Prices).Select(m => m.Key).ToList();
        Assert.True(keys.IndexOf("worth:MelvorBase:GP") < keys.IndexOf("value:MelvorBase:GP"));
        var worth = Metrics.Estimate(result, "worth:MelvorBase:GP", TimeUnit.Kill, Prices);
        Assert.NotNull(worth);
        Assert.Equal((2 * 50 + 30) / 10.0, worth.Point, 9);
    }

    [Fact]
    public void EachCurrencysRowsStandTogether()
    {
        var result = Result(
            (t, _) =>
            {
                t.Gained["MelvorBase:Gem"] = 2;
                t.Currency["MelvorBase:GP"] = 30;
                t.Currency["MelvorBase:Coins"] = 4;
                t.Buryable = new() { ["MelvorBase:Prayer"] = 6 };
            }
        );
        var money = Metrics
            .For([result], Prices)
            .Select(m => m.Key)
            .Where(k => k.Split(':')[0] is "worth" or "value" or "currency" or "bury")
            .ToList();
        Assert.Equal(
            [
                "currency:MelvorBase:Coins",
                "worth:MelvorBase:GP",
                "value:MelvorBase:GP",
                "currency:MelvorBase:GP",
                "bury:MelvorBase:Prayer",
            ],
            money
        );
    }

    [Fact]
    public void DropsCountEveryItemTogether()
    {
        var result = Result(
            (t, _) =>
            {
                t.Gained["MelvorBase:Bones"] = 10;
                t.Gained["MelvorBase:Gem"] = 5;
            }
        );
        Assert.Contains(Metrics.Drops, Metrics.For([result]).Select(m => m.Key));
        var drops = Metrics.Estimate(result, Metrics.Drops, TimeUnit.Kill);
        Assert.NotNull(drops);
        Assert.Equal(1.5, drops.Point, 9);
    }

    [Fact]
    public void HighestHitAndLowestHpAreTheWorstOverTheTrials()
    {
        var result = Result(
            (t, b) =>
            {
                t.MaxHit = 20 + b;
                t.MinHp = 100 - b;
            }
        );
        var keys = Metrics.For([result]).Select(m => m.Key).ToList();
        Assert.Contains(Metrics.HighestHit, keys);
        Assert.Contains(Metrics.LowestHp, keys);
        Assert.Equal(49, Metrics.Estimate(result, Metrics.HighestHit, TimeUnit.Hour)!.Point);
        Assert.Equal(71, Metrics.Estimate(result, Metrics.LowestHp, TimeUnit.Hour)!.Point);
        Assert.Null(Metrics.Series(result, Metrics.HighestHit, TimeUnit.Hour));
        Assert.True(Ranking.LowerIsBetter(Metrics.HighestHit));
        Assert.False(Ranking.LowerIsBetter(Metrics.LowestHp));
    }

    [Fact]
    public void FoodAndConsumablesTotalsAreNamedAndCostsLowerBetter()
    {
        Assert.Equal("Used: Food", Metrics.Describe("used:food").Label);
        Assert.Equal("Used: Consumables", Metrics.Describe("used:consumables").Label);
        Assert.True(Ranking.LowerIsBetter("used:food"));
        Assert.Equal("Used: Potions", Metrics.Describe("used:potions").Label);
        Assert.Equal("Used: Runes", Metrics.Describe("used:runes").Label);
        Assert.Equal("Used: Ammo", Metrics.Describe("used:ammo").Label);
        Assert.True(Ranking.LowerIsBetter("used:ammo"));
    }

    [Fact]
    public void WhatBonesCouldGiveIsAMetricPerCurrency()
    {
        var result = Result((t, _) => t.Buryable = new() { ["MelvorBase:PrayerPoints"] = 6 });
        var keys = Metrics.For([result]).Select(m => m.Key).ToList();
        Assert.Contains("bury:MelvorBase:PrayerPoints", keys);
        Assert.Equal(0.6, Metrics.Estimate(result, "bury:MelvorBase:PrayerPoints", TimeUnit.Kill)!.Point, 9);
    }

    [Fact]
    public void HighestReflectIsListedOnlyWhenSomethingReflected()
    {
        var none = Result((t, _) => t.MaxReflect = 0);
        Assert.DoesNotContain(Metrics.HighestReflect, Metrics.For([none]).Select(m => m.Key));
        var some = Result((t, b) => t.MaxReflect = b);
        Assert.Contains(Metrics.HighestReflect, Metrics.For([some]).Select(m => m.Key));
        Assert.Equal(29, Metrics.Estimate(some, Metrics.HighestReflect, TimeUnit.Hour)!.Point);
        Assert.True(Ranking.LowerIsBetter(Metrics.HighestReflect));
    }

    [Fact]
    public void UnfinishedResultsAddNoMetricsAndNoEstimates()
    {
        var result = Result((t, _) => t.Gained["X:Y"] = 1);
        result.State = TargetState.Unfinished;
        result.Reasons.Add(Note.Text("a trial ran over 300 s"));
        Assert.DoesNotContain(Metrics.For([result]), m => m.Key == "item:X:Y");
        Assert.Null(Metrics.Estimate(result, "kills", TimeUnit.Hour, Prices));
    }

    [Fact]
    public void ValueSumsPerCurrencyAndDirectCurrencyStaysApart()
    {
        var result = Result(
            (t, _) =>
            {
                t.Gained["MelvorBase:Bones"] = 10; // 10 GP
                t.Gained["MelvorBase:Gem"] = 1; // 50 GP
                t.Gained["MelvorBase:Token"] = 2; // 6 SC
                t.Gained["MelvorBase:Unsellable"] = 4;
                t.Currency["MelvorBase:GP"] = 100;
            }
        );
        var keys = Metrics.For([result], Prices).Select(m => m.Key).ToList();
        Assert.Contains("value:MelvorBase:GP", keys);
        Assert.Contains("value:MelvorBase:SlayerCoins", keys);
        Assert.Contains("currency:MelvorBase:GP", keys);

        var gp = Metrics.Estimate(result, "value:MelvorBase:GP", TimeUnit.Kill, Prices)!;
        Assert.Equal(6, gp.Point, 9); // (10 + 50) per 10 kills
        var sc = Metrics.Estimate(result, "value:MelvorBase:SlayerCoins", TimeUnit.Kill, Prices)!;
        Assert.Equal(0.6, sc.Point, 9);
        var dropped = Metrics.Estimate(result, "currency:MelvorBase:GP", TimeUnit.Kill, Prices)!;
        Assert.Equal(10, dropped.Point, 9);
    }

    [Fact]
    public void TimeUnitsScaleRates()
    {
        var result = Result((t, _) => t.Xp["MelvorBase:Attack"] = 12);
        var perSecond = Metrics.Estimate(result, "xp:MelvorBase:Attack", TimeUnit.Second, Prices)!;
        var perHour = Metrics.Estimate(result, "xp:MelvorBase:Attack", TimeUnit.Hour, Prices)!;
        Assert.Equal(perSecond.Point * 3600, perHour.Point, 6);
        Assert.Equal(perSecond.High * 3600, perHour.High, 6);
    }

    [Fact]
    public void UntimedMetricsIgnoreTheUnit()
    {
        var result = Result((t, b) => t.Deaths = b % 3 == 0 ? 1 : 0);
        var metrics = Metrics.For([result]).ToDictionary(m => m.Key);
        Assert.False(metrics["deathRate"].Timed);
        Assert.False(metrics["killTime"].Timed);
        Assert.True(metrics["kills"].Timed);
        var rate = Metrics.Estimate(result, "deathRate", TimeUnit.Hour, Prices)!;
        Assert.Equal(rate, Metrics.Estimate(result, "deathRate", TimeUnit.Second, Prices));
        Assert.Equal("wilson", rate.Method);
        Assert.Equal(10 / 300.0, rate.Point, 9);
        var killTime = Metrics.Estimate(result, "killTime", TimeUnit.Hour, Prices)!;
        Assert.InRange(killTime.Point, 6, 6.4);
    }

    [Fact]
    public void KillTimeWithNoKillsIsUnboundedNotZero()
    {
        var result = Result(
            (t, _) =>
            {
                t.Kills = 0;
                t.Deaths = 10;
            }
        );
        var e = Metrics.Estimate(result, "killTime", TimeUnit.Hour, Prices)!;
        Assert.True(double.IsPositiveInfinity(e.Point));
    }

    [Fact]
    public void TrialsNeededComesFromKillsPerTime()
    {
        var result = Result((_, _) => { });
        Assert.InRange(Metrics.TrialsFor5Percent(result), 1, int.MaxValue - 1);
    }

    [Fact]
    public void AllGivesEveryMetricOfAResult()
    {
        var result = Result(
            (t, _) =>
            {
                t.Gained["MelvorBase:Bones"] = 1;
                t.Currency["MelvorBase:GP"] = 3;
            }
        );
        var all = Metrics.All(result, TimeUnit.Hour, Prices);
        Assert.Equal(Metrics.For([result], Prices).Select(m => m.Key), all.Select(v => v.Metric.Key));
        Assert.All(all, v => Assert.True(v.Estimate.Low <= v.Estimate.Point && v.Estimate.Point <= v.Estimate.High));
    }

    [Fact]
    public void NamesComeFromTheLookup()
    {
        var result = Result((t, _) => t.Xp["MelvorBase:Attack"] = 1);
        var metric = Metrics
            .For([result], names: id => id == "MelvorBase:Attack" ? "Attack" : id)
            .Single(m => m.Key == "xp:MelvorBase:Attack");
        Assert.Equal("XP: Attack", metric.Label);
    }

    [Fact]
    public void EstimateIsUsedNotAnotherType() =>
        Assert.IsType<Estimate>(Metrics.Estimate(Result((_, _) => { }), "kills", TimeUnit.Hour, Prices));

    [Fact]
    public void MainXpFallsBackToTheSkillWithTheMostXpOtherThanHitpoints()
    {
        var run = RankingTests.RunOf([
            RankingTests.Finished("a", "A", 10),
            Result(
                (t, _) =>
                {
                    t.Xp["MelvorBase:Attack"] = 15;
                    t.Xp["MelvorBase:Hitpoints"] = 500;
                }
            ),
        ]);
        // Strength: 30 × 10 = 300 in "a"; Attack: 30 × 15 = 450 in the other; Hitpoints is never main.
        Assert.Equal("xp:MelvorBase:Attack", Metrics.Resolve(run, Metrics.MainXp));
        run.MainSkill = "MelvorBase:Strength";
        Assert.Equal("xp:MelvorBase:Strength", Metrics.Resolve(run, Metrics.MainXp));
        Assert.Equal("kills", Metrics.Resolve(run, "kills"));
        Assert.Equal("kills", Metrics.Resolve(RankingTests.RunOf([]), Metrics.MainXp));
    }
}
