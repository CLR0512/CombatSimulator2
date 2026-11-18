using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class DifferencesTests
{
    private static Estimate E(double point, double se) =>
        new(point, point - Estimators.Z95 * se, point + Estimators.Z95 * se, 30, "ratio");

    [Fact]
    public void IndependentAddsVariances()
    {
        var d = Differences.Independent(E(10, 3), E(15, 4));
        Assert.Equal(5, d.Point, 9);
        Assert.Equal(5, d.StandardError, 9);
        Assert.Equal(5 - Estimators.Z95 * 5, d.Low, 9);
        Assert.Equal(5 + Estimators.Z95 * 5, d.High, 9);
    }

    [Fact]
    public void IndependentWithAnUnboundedSideIsUnbounded()
    {
        var a = new Estimate(10, 0, double.PositiveInfinity, 1, "ratio");
        var d = Differences.Independent(a, E(12, 1));
        Assert.True(double.IsNegativeInfinity(d.Low));
        Assert.True(double.IsPositiveInfinity(d.High));
        Assert.Equal(Verdict.NoClearDifference, Differences.VerdictOf(d, false));
    }

    [Fact]
    public void PairedEqualsIndependentWhenResidualsAreUncorrelated()
    {
        // A's residuals alternate +1/−1 in pairs, B's +1/−1 singly: their products sum to 0.
        double[] ta = [1, 1, 1, 1, 1, 1, 1, 1];
        double[] va = [6, 6, 4, 4, 6, 6, 4, 4];
        double[] vb = [9, 7, 9, 7, 9, 7, 9, 7];
        var paired = Differences.Paired(va, ta, vb, ta)!;
        var independent = Differences.Independent(Estimators.RatioOfBatches(va, ta), Estimators.RatioOfBatches(vb, ta));
        Assert.Equal(independent.Point, paired.Point, 9);
        Assert.Equal(independent.Low, paired.Low, 9);
        Assert.Equal(independent.High, paired.High, 9);
    }

    [Fact]
    public void PairedIsNarrowerWhenSidesMoveTogether()
    {
        double[] t = [1, 1, 1, 1, 1, 1];
        double[] va = [4, 6, 5, 7, 3, 5];
        double[] vb = [5, 7, 6, 8, 4, 6.5];
        var paired = Differences.Paired(va, t, vb, t)!;
        var independent = Differences.Independent(Estimators.RatioOfBatches(va, t), Estimators.RatioOfBatches(vb, t));
        Assert.True(paired.High - paired.Low < (independent.High - independent.Low) / 3);
        Assert.Equal(Verdict.Better, Differences.VerdictOf(paired, false));
    }

    [Fact]
    public void PairedRefusesUnequalOrTooFewBatches()
    {
        Assert.Null(Differences.Paired([1, 2, 3], [1, 1, 1], [1, 2], [1, 1]));
        Assert.Null(Differences.Paired([1], [1], [1], [1]));
    }

    [Fact]
    public void NewcombeMatchesThePublishedExample()
    {
        // 56/70 against 48/80: Newcombe (1998), method 10, 0.0524 to 0.3339.
        var a = Estimators.Wilson(48, 80);
        var b = Estimators.Wilson(56, 70);
        var d = Differences.Newcombe(a, b);
        Assert.Equal(0.2, d.Point, 9);
        Assert.Equal(0.0524, d.Low, 3);
        Assert.Equal(0.3339, d.High, 3);
    }

    [Fact]
    public void NewcombeAtNoDeathsOnEitherSideIncludesZero()
    {
        var d = Differences.Newcombe(Estimators.Wilson(0, 1000), Estimators.Wilson(0, 1000));
        Assert.Equal(0, d.Point);
        Assert.True(d.Low < 0 && d.High > 0);
        Assert.Equal(Verdict.NoClearDifference, Differences.VerdictOf(d, true));
    }

    [Fact]
    public void NewcombeFromNoneToAllIsClear()
    {
        var d = Differences.Newcombe(Estimators.Wilson(0, 100), Estimators.Wilson(100, 100));
        Assert.Equal(1, d.Point, 9);
        Assert.True(d.Low > 0.9);
        Assert.Equal(Verdict.Worse, Differences.VerdictOf(d, true));
    }

    [Fact]
    public void PercentUsesTheLogRatio()
    {
        var p = Differences.Percent(E(100, 5), E(120, 6))!;
        Assert.Equal(0.2, p.Point, 9);
        var se = Math.Sqrt(0.05 * 0.05 + 0.05 * 0.05);
        Assert.Equal(Math.Exp(Math.Log(1.2) - Estimators.Z95 * se) - 1, p.Low, 9);
        Assert.Equal(Math.Exp(Math.Log(1.2) + Estimators.Z95 * se) - 1, p.High, 9);
    }

    [Fact]
    public void PercentNeverGoesBelowMinusOneAndNeedsPositivePoints()
    {
        var p = Differences.Percent(E(100, 40), E(1, 0.9))!;
        Assert.True(p.Low > -1);
        Assert.Null(Differences.Percent(E(0, 1), E(1, 1)));
        Assert.Null(Differences.Percent(E(1, 1), E(0, 1)));
        Assert.Null(Differences.Percent(new Estimate(double.PositiveInfinity, 0, 0, 1, "ratio"), E(1, 1)));
    }

    [Theory]
    [InlineData(1, 3, false, Verdict.Better)]
    [InlineData(1, 3, true, Verdict.Worse)]
    [InlineData(-3, -1, false, Verdict.Worse)]
    [InlineData(-3, -1, true, Verdict.Better)]
    [InlineData(-1, 1, false, Verdict.NoClearDifference)]
    [InlineData(0, 1, false, Verdict.NoClearDifference)]
    [InlineData(0, 0, true, Verdict.NoClearDifference)]
    public void VerdictJudgesTheWholeRange(double low, double high, bool lowerIsBetter, Verdict expected) =>
        Assert.Equal(
            expected,
            Differences.VerdictOf(new Estimate((low + high) / 2, low, high, 30, "independent"), lowerIsBetter)
        );

    [Fact]
    public void NoDeltaMeansNoVerdict() => Assert.Equal(Verdict.None, Differences.VerdictOf(null, false));

    [Fact]
    public void RunsCompareTargetByTargetInAsOrder()
    {
        var a = RankingTests.RunOf([
            RankingTests.Finished("t:1", "One", 100),
            RankingTests.Finished("t:2", "Two", 100),
        ]);
        var b = RankingTests.RunOf([
            RankingTests.Finished("t:2", "Two", 100),
            RankingTests.Finished("t:1", "One", 150),
        ]);
        var diffs = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour);
        Assert.Equal(["t:1", "t:2"], diffs.Select(d => d.Key));
        Assert.Equal(Verdict.Better, diffs[0].Verdict);
        Assert.True(diffs[0].Improvement > 0);
        Assert.Equal(0.5, diffs[0].Percent!.Point, 9);
        // Identical sides: a difference of exactly 0.
        Assert.Equal(0, diffs[1].Delta!.Point);
        Assert.Equal(Verdict.NoClearDifference, diffs[1].Verdict);
    }

    [Fact]
    public void LowerIsBetterMetricsFlip()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100, seconds: 60)]);
        var b = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100, seconds: 30)]);
        var d = Differences.Of(a, b, Metrics.KillTime, TimeUnit.Hour).Single();
        Assert.True(d.LowerIsBetter);
        Assert.True(d.Delta!.Point < 0);
        Assert.Equal(Verdict.Better, d.Verdict);
    }

    [Fact]
    public void ASideWithoutAnEstimateGivesItsReasonAndNoDifference()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100)]);
        var b = RankingTests.RunOf([RankingTests.Other("t:1", "One", TargetState.Refused, "too weak")]);
        var d = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour).Single();
        Assert.Null(d.Delta);
        Assert.Null(d.ReasonsA);
        Assert.Equal("too weak", d.ReasonsB!.English("; "));
        Assert.Equal(Verdict.None, d.Verdict);
    }

    [Fact]
    public void NoKillsOnASideGivesNoTimePerKillDifference()
    {
        var none = RankingTests.Finished("t:1", "One", 0);
        foreach (var tally in none.Batches)
            tally.Kills = 0;
        var a = RankingTests.RunOf([none]);
        var b = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100)]);
        var d = Differences.Of(a, b, Metrics.KillTime, TimeUnit.Hour).Single();
        Assert.True(double.IsPositiveInfinity(d.A!.Point));
        Assert.Null(d.Delta);
        Assert.Equal(Verdict.None, d.Verdict);
    }

    [Fact]
    public void BothSidesZeroIsNoClearDifference()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 0)]);
        var b = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 0)]);
        var d = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour).Single();
        Assert.Equal(0, d.Delta!.Point);
        Assert.Null(d.Percent);
        Assert.Equal(Verdict.NoClearDifference, d.Verdict);
    }

    [Fact]
    public void DifferentMainSkillsShowADifferenceWithoutAVerdict()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100)]);
        a.MainSkill = "MelvorBase:Strength";
        var ranged = RankingTests.Finished("t:1", "One", 0);
        foreach (var tally in ranged.Batches)
            tally.Xp["MelvorBase:Ranged"] = 300;
        var b = RankingTests.RunOf([ranged]);
        b.MainSkill = "MelvorBase:Ranged";
        var d = Differences.Of(a, b, Metrics.MainXp, TimeUnit.Hour).Single();
        Assert.True(d.SkillsDiffer);
        Assert.NotNull(d.Delta);
        Assert.Equal(Verdict.None, d.Verdict);
    }

    [Fact]
    public void PairedMethodIsUsedWhenAsked()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100)]);
        var b = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 120)]);
        var d = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour, method: DifferenceMethod.Paired).Single();
        Assert.Equal("paired", d.Delta!.Method);
        var death = Differences.Of(a, b, Metrics.DeathRate, TimeUnit.Hour, method: DifferenceMethod.Paired).Single();
        Assert.Equal("newcombe", death.Delta!.Method);
    }

    [Fact]
    public void PairedIsTheDefaultAndFallsBackWhenBatchCountsDiffer()
    {
        Assert.Equal(DifferenceMethod.Paired, Differences.Method);
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100)]);
        var stopped = RankingTests.Finished("t:1", "One", 120);
        stopped.Batches.RemoveRange(20, 10); // stopped early: fewer batches than A
        var b = RankingTests.RunOf([stopped]);
        Assert.Equal(
            "independent",
            Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour).Single().Delta!.Method
        );
        var full = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 120)]);
        Assert.Equal("paired", Differences.Of(a, full, "xp:MelvorBase:Strength", TimeUnit.Hour).Single().Delta!.Method);
    }

    [Fact]
    public void PercentWithADifferenceFollowsItsRange()
    {
        // The percentage's range is the difference's range relative to A: it holds 0 exactly when the difference does.
        var a = E(100, 5);
        var b = E(104, 5);
        var delta = new Estimate(4, -1, 9, 30, "paired");
        var p = Differences.Percent(a, b, delta)!;
        Assert.Equal(0.04, p.Point, 9);
        Assert.Equal(-0.01, p.Low, 9);
        Assert.Equal(0.09, p.High, 9);
        var identical = Differences.Percent(a, a, new Estimate(0, 0, 0, 30, "paired"))!;
        Assert.Equal((0.0, 0.0), (identical.Low, identical.High));
        Assert.Equal(-1, Differences.Percent(a, E(10, 1), new Estimate(-90, -150, -30, 30, "independent"))!.Low);
    }

    [Theory]
    [InlineData(true, false, "A died every fight")]
    [InlineData(false, true, "B died every fight")]
    [InlineData(true, true, "Both died every fight")]
    public void ASideThatDiedEveryTrialGivesNoVerdictAndSaysWhy(bool diedA, bool diedB, string reason)
    {
        // B earns half as much again, a clear "better" between two survivors.
        var a = RankingTests.RunOf([
            diedA ? RankingTests.Died("t:1", "One", 100) : RankingTests.Finished("t:1", "One", 100),
        ]);
        var b = RankingTests.RunOf([
            diedB ? RankingTests.Died("t:1", "One", 150) : RankingTests.Finished("t:1", "One", 150),
        ]);
        var d = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour).Single();
        Assert.Equal((diedA, diedB), (d.DiedA, d.DiedB));
        Assert.Equal(Verdict.None, d.Verdict);
        Assert.Equal(reason, d.Reason!.English());
        // The values and their difference are still there to read.
        Assert.Equal(0.5, d.Percent!.Point, 9);
        Assert.NotNull(d.Delta);
        Assert.Null(d.ReasonsA);
        Assert.Null(d.ReasonsB);
    }

    [Fact]
    public void SurvivedSidesHaveNoDiedReason()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100, deaths: 9)]);
        var b = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 150)]);
        var d = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour).Single();
        Assert.Equal((false, false), (d.DiedA, d.DiedB));
        Assert.Null(d.Reason);
        Assert.Equal(Verdict.Better, d.Verdict);
    }

    [Fact]
    public void TheDiedReasonHoldsForEveryMetricOfATarget()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("t:1", "One", 100)]);
        var b = RankingTests.RunOf([RankingTests.Died("t:1", "One", 150, seconds: 30)]);
        var rows = Differences.ForTarget(a, b, "t:1", ["xp:MelvorBase:Strength", Metrics.KillTime], TimeUnit.Hour);
        Assert.All(rows, d => Assert.Equal(Verdict.None, d.Verdict));
        Assert.All(rows, d => Assert.Equal("B died every fight", d.Reason!.English()));
    }

    [Fact]
    public void OnlyTheSideThatCanKillInTimeIsBetter()
    {
        var stalled = StallGroupsTests.Stalled("area/one", 10, 5, 50);
        stalled.Reasons = [StallReasons.Note(stalled.Stall!)];
        var a = RankingTests.RunOf([stalled, RankingTests.Finished("area/two", "Two", 100)]);
        var b = RankingTests.RunOf([
            RankingTests.Finished("area/one", "One", 100),
            RankingTests.Finished("area/two", "Two", 200),
        ]);
        var d = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour)[0];
        Assert.Equal(Verdict.Better, d.Verdict);
        Assert.Equal("B", d.OnlyKills);
        Assert.Null(d.Delta);
        Assert.Equal(StallReason.TooSlow, d.StallA!.Reason);
        Assert.Null(d.StallB);
        Assert.StartsWith("Too slow:", d.ReasonsA!.English("; "));
        var better = CompareTables.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour, filter: CompareFilter.Better);
        Assert.Contains(better.Rows, r => r.Difference.Key == "area/one");

        // Lower-is-better metrics too: a side that can't kill has no kill time to beat.
        Assert.All(
            Differences.ForTarget(a, b, "area/one", [Metrics.KillTime], TimeUnit.Hour),
            k => Assert.Equal(Verdict.Better, k.Verdict)
        );

        var reverse = Differences.Of(b, a, "xp:MelvorBase:Strength", TimeUnit.Hour)[0];
        Assert.Equal(Verdict.Worse, reverse.Verdict);
        Assert.Equal("A", reverse.OnlyKills);
    }

    [Fact]
    public void NeitherSideKillingInTimeHasNoVerdict()
    {
        var a = RankingTests.RunOf([StallGroupsTests.Stalled("area/one", 10, 5, 50)]);
        var b = RankingTests.RunOf([StallGroupsTests.Stalled("area/one", 10, 0, 90)]);
        var d = Differences.Of(a, b, "xp:MelvorBase:Strength", TimeUnit.Hour)[0];
        Assert.Equal(Verdict.None, d.Verdict);
        Assert.Null(d.OnlyKills);
    }

    // Batch i of a side lands hits(i) of 100 attacks.
    private static TargetResult Hitting(Func<int, long> hits)
    {
        var r = RankingTests.Finished("area/one", "One", 100);
        for (var i = 0; i < r.Batches.Count; i++)
            r.Batches[i].Fight = new FightCounts
            {
                Dealt = new SideCounts
                {
                    Attacks = 100,
                    Hits = hits(i),
                    Attack = hits(i) * 10,
                },
                Seconds = 60,
            };
        return r;
    }

    private static Difference Fight(TargetResult a, TargetResult b, string key) =>
        Differences
            .ForTarget(RankingTests.RunOf([a]), RankingTests.RunOf([b]), "area/one", [key], TimeUnit.Hour)
            .Single();

    [Fact]
    public void AFightNumberHasAPairedDifference()
    {
        var clear = Fight(Hitting(i => 40 + i % 3), Hitting(i => 50 + i % 3), FightNumbers.Hit);
        Assert.Equal(Verdict.Better, clear.Verdict);
        Assert.Equal(0.1, clear.Delta!.Point, 10);
        var unclear = Fight(Hitting(i => 40 + i % 5), Hitting(i => 40 + (i + 2) % 5), FightNumbers.PerHit);
        Assert.Equal(Verdict.NoClearDifference, unclear.Verdict);
    }

    [Fact]
    public void ASideWithoutCountsOrStalledHasNoDifference()
    {
        var old = Fight(RankingTests.Finished("area/one", "One", 100), Hitting(i => 50), FightNumbers.Hit);
        Assert.Null(old.A);
        Assert.Null(old.Delta);
        Assert.Equal(Verdict.None, old.Verdict);
        var stalled = Fight(StallGroupsTests.Stalled("area/one", 10, 0, 100), Hitting(i => 50), FightNumbers.Hit);
        Assert.Null(stalled.Delta);
        Assert.NotNull(stalled.StallA);
    }
}
