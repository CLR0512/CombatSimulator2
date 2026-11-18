using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// Difference ranges on synthetic runs drawn from known distributions: they must cover the true difference about
/// 95% of the time, so equal setups are called better or worse only about 5% of the time. Seeded.
/// </summary>
public sealed class CalibrationTests
{
    private const int Comparisons = 2000;
    private const int BatchCount = 30;
    private const int TrialsPerBatch = 33;

    // One synthetic batch: each trial is a kill after a gamma-distributed time, or a death with a set chance.
    private static (double Kills, double Seconds, int Deaths) Batch(Random rng, double meanSeconds, double deathChance)
    {
        double kills = 0,
            seconds = 0;
        var deaths = 0;
        for (var t = 0; t < TrialsPerBatch; t++)
        {
            // Gamma(3, mean/3) as a sum of three exponentials.
            for (var k = 0; k < 3; k++)
                seconds += -Math.Log(1 - rng.NextDouble()) * meanSeconds / 3;
            if (rng.NextDouble() < deathChance)
                deaths++;
            else
                kills++;
        }
        return (kills, seconds, deaths);
    }

    private static (Estimate Rate, Estimate KillTime, Estimate DeathRate, double[] V, double[] T) Side(
        Random rng,
        double meanSeconds,
        double deathChance
    )
    {
        var v = new double[BatchCount];
        var t = new double[BatchCount];
        var deaths = 0;
        for (var b = 0; b < BatchCount; b++)
        {
            var (k, s, d) = Batch(rng, meanSeconds, deathChance);
            v[b] = k;
            t[b] = s;
            deaths += d;
        }
        return (
            Estimators.RatioOfBatches(v, t),
            Estimators.RatioOfBatches(t, v),
            Estimators.Wilson(deaths, BatchCount * TrialsPerBatch),
            v,
            t
        );
    }

    [Theory]
    [InlineData(20.0, 0.02, 20.0, 0.02)] // equal sides
    [InlineData(20.0, 0.02, 22.0, 0.05)] // a real difference
    [InlineData(8.0, 0.0, 8.0, 0.0)] // fast kills, no deaths
    public void RangesCoverTheTrueDifference(double secondsA, double deathA, double secondsB, double deathB)
    {
        var rng = new Random(20261107);
        // True values: kills per second = (1 − p) / mean, seconds per kill = mean / (1 − p), death rate = p.
        var trueRate = (1 - deathB) / secondsB - (1 - deathA) / secondsA;
        var trueTime = secondsB / (1 - deathB) - secondsA / (1 - deathA);
        var trueDeath = deathB - deathA;
        int rateHits = 0,
            timeHits = 0,
            deathHits = 0,
            pairedHits = 0;
        for (var i = 0; i < Comparisons; i++)
        {
            var a = Side(rng, secondsA, deathA);
            var b = Side(rng, secondsB, deathB);
            if (Covers(Differences.Independent(a.Rate, b.Rate), trueRate))
                rateHits++;
            if (Covers(Differences.Independent(a.KillTime, b.KillTime), trueTime))
                timeHits++;
            if (Covers(Differences.Newcombe(a.DeathRate, b.DeathRate), trueDeath))
                deathHits++;
            if (Covers(Differences.Paired(a.V, a.T, b.V, b.T)!, trueRate))
                pairedHits++;
        }
        AssertCalibrated(rateHits, "kills rate");
        AssertCalibrated(timeHits, "time per kill");
        AssertCalibrated(pairedHits, "paired kills rate");
        if (deathA > 0 || deathB > 0)
            AssertCalibrated(deathHits, "death rate");
        else
            Assert.Equal(Comparisons, deathHits); // no deaths anywhere: the range always holds 0
    }

    [Fact]
    public void EqualSidesAreCalledBetterOrWorseAboutOneInTwenty()
    {
        var rng = new Random(7);
        var called = 0;
        for (var i = 0; i < Comparisons; i++)
        {
            var a = Side(rng, 15, 0.03);
            var b = Side(rng, 15, 0.03);
            if (
                Differences.VerdictOf(Differences.Independent(a.Rate, b.Rate), false) is Verdict.Better or Verdict.Worse
            )
                called++;
        }
        var share = called / (double)Comparisons;
        Assert.InRange(share, 0.02, 0.08);
    }

    private static bool Covers(Estimate range, double truth) => range.Low <= truth && truth <= range.High;

    private static void AssertCalibrated(int hits, string what)
    {
        var coverage = hits / (double)Comparisons;
        Assert.True(coverage is >= 0.93 and <= 0.97, $"{what}: coverage {coverage:P1}");
    }
}
