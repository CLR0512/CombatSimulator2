using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class FightMetricsTests
{
    // A finished target with counts: chance to hit hits/100, damage per second dps, damage taken per second taken.
    internal static TargetResult Counted(string key, long hits, double dps, double taken, int deaths = 0)
    {
        var r = RankingTests.Finished(key, key, 10, deaths: deaths);
        foreach (var b in r.Batches)
            b.Fight = new FightCounts
            {
                Dealt = new SideCounts
                {
                    Attacks = 100,
                    Hits = hits,
                    Attack = (long)(dps * 50),
                },
                Taken = new SideCounts
                {
                    Attacks = 10,
                    Hits = 5,
                    Attack = (long)(taken * 50),
                },
                Seconds = 50,
            };
        return r;
    }

    [Fact]
    public void TheThreeStatsAreOfferedOnlyWithCounts()
    {
        var keys = Metrics.For([Counted("a", 40, 10, 2)]).Select(m => m.Key).ToList();
        Assert.Equal([FightNumbers.Hit, FightNumbers.Dps, FightNumbers.Taken], keys.Skip(4).Take(3));
        Assert.DoesNotContain(Metrics.For([RankingTests.Finished("a", "a", 10)]), m => FightNumbers.IsFightKey(m.Key));
        Assert.All(
            Metrics.For([Counted("a", 40, 10, 2)]).Where(m => FightNumbers.IsFightKey(m.Key)),
            m => Assert.False(m.Timed)
        );
        Assert.Equal("Chance to hit", Metrics.Describe(FightNumbers.Hit).Label);
        Assert.Equal("Damage taken per second", Metrics.Describe(FightNumbers.Taken).Label);
    }

    [Fact]
    public void RankingByEachStatFollowsItsValue()
    {
        var run = RankingTests.RunOf([Counted("a", 40, 10, 5), Counted("b", 60, 5, 2), Counted("c", 50, 20, 9)]);
        Assert.Equal(["b", "c", "a"], Keys(FightNumbers.Hit));
        Assert.Equal(["c", "a", "b"], Keys(FightNumbers.Dps));
        Assert.Equal(["b", "a", "c"], Keys(FightNumbers.Taken));
        Assert.True(Ranking.LowerIsBetter(FightNumbers.Taken));
        Assert.Equal("b", Ranking.Rank(run, FightNumbers.Taken, TimeUnit.Hour).BestKey);
        string[] Keys(string metric) => [.. Ranking.Rank(run, metric, TimeUnit.Hour).Rows.Select(r => r.Key)];
    }

    [Fact]
    public void AnEnemyThatNeverAttackedTakesNothing()
    {
        var calm = Counted("calm", 40, 10, 0);
        foreach (var b in calm.Batches)
            b.Fight!.Taken = new SideCounts();
        Assert.Equal(0, Metrics.Estimate(calm, FightNumbers.Taken, TimeUnit.Hour)!.Point);
        Assert.Null(Metrics.Estimate(calm, FightNumbers.EnemyHit, TimeUnit.Hour));
    }

    [Fact]
    public void ADiedTargetStaysInItsGroup()
    {
        var run = RankingTests.RunOf([Counted("died", 90, 90, 1, deaths: 10), Counted("a", 40, 10, 5)]);
        var table = Ranking.Rank(run, FightNumbers.Hit, TimeUnit.Hour);
        Assert.Equal(["a", "died"], table.Rows.Select(r => r.Key));
        Assert.True(table.Rows[1].Died);
        Assert.Equal("a", table.BestKey);
    }

    [Fact]
    public void AnOldRunOffersNoneAndRanksNothingByThem()
    {
        var run = RunFile.Read(RunFileFormatTests.Format2Run);
        Assert.DoesNotContain(Metrics.For(run.Results.Values), m => FightNumbers.IsFightKey(m.Key));
        Assert.Equal(0, Ranking.Rank(run, FightNumbers.Hit, TimeUnit.Hour).Count);
    }

    [Fact]
    public void SeriesAnswerTheFightKeys()
    {
        var series = Metrics.Series(Counted("a", 40, 10, 2), FightNumbers.Hit, TimeUnit.Hour)!.Value;
        Assert.Equal(40, series.Values[0]);
        Assert.Equal(100, series.Denominators[0]);
    }

    [Fact]
    public void WhatTheEnemyDoesAndWhatAKillCostsAreBetterLower()
    {
        foreach (
            var key in new[]
            {
                FightNumbers.EnemyHit,
                FightNumbers.EnemyPerHit,
                FightNumbers.Taken,
                FightNumbers.Overkill,
                FightNumbers.AttacksPerKill,
                FightNumbers.MissedPerKill,
                FightNumbers.RanOver,
            }
        )
            Assert.True(Ranking.LowerIsBetter(key), key);
        foreach (var key in new[] { FightNumbers.Hit, FightNumbers.PerHit, FightNumbers.Dps })
            Assert.False(Ranking.LowerIsBetter(key), key);
    }
}
