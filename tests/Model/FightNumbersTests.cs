using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class FightNumbersTests
{
    // Ten batches of ten kills; batch i has its own spread so the ranges have a width.
    private static TargetResult WithCounts(int deaths = 0, Action<FightCounts, int>? change = null)
    {
        var result = new TargetResult
        {
            Key = "a/b",
            State = TargetState.Finished,
            TrialsDone = 100,
        };
        for (var i = 0; i < 10; i++)
        {
            var fight = new FightCounts
            {
                Dealt = new SideCounts
                {
                    Attacks = 100 + i,
                    Hits = 40,
                    Attack = 400,
                    Effect = 50,
                    Reflect = 10,
                    Overkill = 40,
                },
                Taken = new SideCounts
                {
                    Attacks = 80,
                    Hits = 20 + i % 3,
                    Attack = 200,
                    Effect = 100,
                },
                Seconds = 100,
            };
            change?.Invoke(fight, i);
            result.Batches.Add(
                new Tally
                {
                    Trials = 10,
                    Kills = 10 - deaths,
                    Deaths = deaths,
                    GameSeconds = 120,
                    Fight = fight,
                    RanOver = i == 0 ? 1 : 0,
                }
            );
            result.Batches[i].Trials += result.Batches[i].RanOver!.Value;
        }
        return result;
    }

    private static double Point(TargetResult r, string key) => FightNumbers.Estimate(r, key)!.Point;

    [Fact]
    public void SharesAreTheChancesOverkillAndRanOver()
    {
        Assert.Equal(
            [FightNumbers.Hit, FightNumbers.EnemyHit, FightNumbers.Overkill, FightNumbers.RanOver],
            FightNumbers.Keys.Where(FightNumbers.IsShare)
        );
    }

    [Fact]
    public void EachNumberIsItsRatioOfSums()
    {
        var r = WithCounts();
        var attacks = Enumerable.Range(0, 10).Sum(i => 100.0 + i);
        Assert.Equal(400 / attacks, Point(r, FightNumbers.Hit), 10);
        Assert.Equal(10, Point(r, FightNumbers.PerHit), 10);
        Assert.Equal(4.6, Point(r, FightNumbers.Dps), 10);
        Assert.Equal(Enumerable.Range(0, 10).Sum(i => 20.0 + i % 3) / 800, Point(r, FightNumbers.EnemyHit), 10);
        Assert.Equal(2000 / Enumerable.Range(0, 10).Sum(i => 20.0 + i % 3), Point(r, FightNumbers.EnemyPerHit), 10);
        Assert.Equal(3, Point(r, FightNumbers.Taken), 10);
        Assert.Equal(40.0 / 500, Point(r, FightNumbers.Overkill), 10);
        Assert.Equal(attacks / 100, Point(r, FightNumbers.AttacksPerKill), 10);
        Assert.Equal((attacks - 400) / 100, Point(r, FightNumbers.MissedPerKill), 10);
        Assert.Equal(1.0 / 101, Point(r, FightNumbers.RanOver), 10);
        var hit = FightNumbers.Estimate(r, FightNumbers.Hit)!;
        Assert.True(hit.Low < hit.Point && hit.Point < hit.High);
    }

    [Fact]
    public void EffectsAndReflectCountInDamagePerSecondButNotPerHit()
    {
        var plain = WithCounts(change: (f, _) => f.Dealt.Effect = f.Dealt.Reflect = 0);
        Assert.Equal(Point(plain, FightNumbers.PerHit), Point(WithCounts(), FightNumbers.PerHit), 10);
        Assert.Equal(4, Point(plain, FightNumbers.Dps), 10);
        Assert.Equal(0.5, FightNumbers.PerSecond(WithCounts(), s => s.Effect, taken: false)!.Point, 10);
        Assert.Equal(0.1, FightNumbers.PerSecond(WithCounts(), s => s.Reflect, taken: false)!.Point, 10);
        Assert.Null(FightNumbers.PerSecond(plain, s => s.Effect, taken: false));
        Assert.Equal(1, FightNumbers.PerSecond(WithCounts(), s => s.Effect, taken: true)!.Point, 10);
    }

    [Fact]
    public void NothingToMeasureGivesNoNumber()
    {
        var noEnemyAttack = WithCounts(change: (f, _) => f.Taken = new SideCounts());
        Assert.Null(FightNumbers.Estimate(noEnemyAttack, FightNumbers.EnemyHit));
        Assert.Null(FightNumbers.Estimate(noEnemyAttack, FightNumbers.EnemyPerHit));
        Assert.Equal(0, Point(noEnemyAttack, FightNumbers.Taken));
        var noHit = WithCounts(change: (f, _) => f.Dealt = new SideCounts { Attacks = 5 });
        Assert.Equal(0, Point(noHit, FightNumbers.Hit));
        Assert.Null(FightNumbers.Estimate(noHit, FightNumbers.PerHit));
        Assert.Null(FightNumbers.Estimate(WithCounts(deaths: 10), FightNumbers.AttacksPerKill));
    }

    [Fact]
    public void AResultWhereEveryTrialDiedStillHasItsNumbers()
    {
        var died = WithCounts(deaths: 10);
        Assert.NotNull(FightNumbers.Estimate(died, FightNumbers.Hit));
        Assert.NotNull(FightNumbers.Estimate(died, FightNumbers.Taken));
    }

    [Fact]
    public void ADungeonUsesItsWholeClear()
    {
        var dungeon = WithCounts();
        dungeon.Kind = TargetKind.Dungeon;
        Assert.Equal(Point(WithCounts(), FightNumbers.Dps), Point(dungeon, FightNumbers.Dps), 10);
    }

    [Fact]
    public void AStallGivesPlainValues()
    {
        var stall = RanOverTests.OneTrial(10, 100);
        var numbers = FightNumbers.OfStall(stall);
        Assert.Equal(0.4, numbers[FightNumbers.Hit], 10);
        Assert.Equal(15, numbers[FightNumbers.PerHit], 10);
        Assert.Equal(635 / 250.5, numbers[FightNumbers.Dps], 10);
        Assert.False(numbers.ContainsKey(FightNumbers.AttacksPerKill));
        Assert.False(numbers.ContainsKey(FightNumbers.RanOver));
    }

    [Fact]
    public void AnOldResultHasNone()
    {
        foreach (var r in RunFile.Read(RunFileFormatTests.Format2Run).Results.Values)
        foreach (var key in FightNumbers.Keys)
            Assert.Null(FightNumbers.Estimate(r, key));
    }
}
