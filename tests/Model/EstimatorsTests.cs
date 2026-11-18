using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class EstimatorsTests
{
    [Fact]
    public void WilsonAtZeroHasAPositiveUpperBound()
    {
        var e = Estimators.Wilson(0, 100);
        Assert.Equal(0, e.Point, 6);
        Assert.Equal(0, e.Low, 4);
        Assert.Equal(0.0370, e.High, 4);
        Assert.Equal(100, e.N);
    }

    [Fact]
    public void WilsonAtHalf()
    {
        var e = Estimators.Wilson(50, 100);
        Assert.Equal(0.5, e.Point, 6);
        Assert.Equal(0.4038, e.Low, 4);
        Assert.Equal(0.5962, e.High, 4);
    }

    [Fact]
    public void WilsonWithNoTrialsIsTheWholeRange()
    {
        var e = Estimators.Wilson(0, 0);
        Assert.Equal(0, e.Low);
        Assert.Equal(1, e.High);
    }

    [Fact]
    public void ConstantBatchesGiveAZeroWidthRatio()
    {
        double[] values = [3, 3, 3, 3, 3];
        double[] times = [10, 10, 10, 10, 10];
        var e = Estimators.RatioOfBatches(values, times);
        Assert.Equal(0.3, e.Point, 9);
        Assert.Equal(0.3, e.Low, 9);
        Assert.Equal(0.3, e.High, 9);
        Assert.Equal(5, e.N);
    }

    [Fact]
    public void RatioIsTheRatioOfSums()
    {
        double[] values = [1, 2, 3];
        double[] times = [4, 5, 6];
        Assert.Equal(6.0 / 15.0, Estimators.RatioOfBatches(values, times).Point, 12);
    }

    [Fact]
    public void RatioOfNonNegativeValuesNeverGoesBelowZero()
    {
        double[] values = [0, 0, 0, 0, 5];
        double[] times = [1, 1, 1, 1, 1];
        Assert.Equal(0, Estimators.RatioOfBatches(values, times).Low);
    }

    [Fact]
    public void RatioWithNoTimeIsZero()
    {
        var e = Estimators.RatioOfBatches([], []);
        Assert.Equal(0, e.Point);
        Assert.Equal(0, e.N);
    }

    [Fact]
    public void RatioIntervalCoversTheTrueRate()
    {
        // Each trial: a kill after an exponential time with mean 6 s; true rate 1/6 per second.
        const double trueRate = 1.0 / 6.0;
        const int repetitions = 2_000;
        const int batches = 30;
        const int trialsPerBatch = 33;
        var random = new Random(1234);
        var covered = 0;
        var values = new double[batches];
        var times = new double[batches];
        for (var r = 0; r < repetitions; r++)
        {
            for (var b = 0; b < batches; b++)
            {
                values[b] = trialsPerBatch;
                times[b] = 0;
                for (var t = 0; t < trialsPerBatch; t++)
                    times[b] += -Math.Log(1 - random.NextDouble()) / trueRate;
            }
            var e = Estimators.RatioOfBatches(values, times);
            if (e.Low <= trueRate && trueRate <= e.High)
                covered++;
        }
        var coverage = covered / (double)repetitions;
        Assert.InRange(coverage, 0.93, 0.97);
    }

    [Fact]
    public void TrialsForGrowsAsPrecisionTightens()
    {
        var random = new Random(7);
        var values = new double[30];
        var times = new double[30];
        for (var b = 0; b < 30; b++)
        {
            values[b] = 30 + random.Next(-5, 6);
            times[b] = 200 + random.Next(-20, 21);
        }
        var loose = Estimators.TrialsFor(0.10, values, times, 33);
        var tight = Estimators.TrialsFor(0.05, values, times, 33);
        var tighter = Estimators.TrialsFor(0.01, values, times, 33);
        Assert.True(loose < tight, $"{loose} < {tight}");
        Assert.True(tight < tighter, $"{tight} < {tighter}");
    }

    [Fact]
    public void TrialsForAZeroRateIsUnbounded() =>
        Assert.Equal(int.MaxValue, Estimators.TrialsFor(0.05, [0, 0, 0], [1, 1, 1], 10));

    [Fact]
    public void ZIsTheDifferenceOverTheCombinedError()
    {
        Assert.Equal((10 - 7) / Math.Sqrt(4 + 9), Estimators.Z(10, 2, 7, 3), 12);
        Assert.Equal(0, Estimators.Z(5, 0, 5, 0));
    }

    [Fact]
    public void BatchesGroupConsecutiveTrials()
    {
        Assert.Equal(30, Batches.Count(1000));
        Assert.Equal(12, Batches.Count(12));
        Assert.Equal(0, Batches.Count(0));
        Assert.Equal(0, Batches.IndexOf(0, 1000));
        Assert.Equal(29, Batches.IndexOf(999, 1000));
        var sizes = new int[30];
        for (var i = 0; i < 1000; i++)
            sizes[Batches.IndexOf(i, 1000)]++;
        Assert.All(sizes, s => Assert.InRange(s, 33, 34));
        for (var i = 1; i < 1000; i++)
            Assert.True(Batches.IndexOf(i, 1000) >= Batches.IndexOf(i - 1, 1000));
    }

    private static List<Tally> Steady(int count, Func<int, int> kills, double seconds = 100) =>
        [
            .. Enumerable
                .Range(0, count)
                .Select(b => new Tally
                {
                    Trials = 33,
                    Kills = kills(b),
                    GameSeconds = seconds,
                }),
        ];

    [Fact]
    public void EarlyStopWaitsForTenCompleteBatches()
    {
        Assert.False(EarlyStop.ShouldStop(Steady(9, _ => 33), 0.05));
        Assert.True(EarlyStop.ShouldStop(Steady(10, _ => 33), 0.05));
    }

    [Fact]
    public void EarlyStopTreatsRanOverTrialsAsTrials()
    {
        // A ran-over trial is in the batch's time and trials but is no kill: the rate is kills over time as ever.
        var plain = Steady(10, _ => 33);
        var ranOver = Steady(10, _ => 33);
        foreach (var b in ranOver)
        {
            b.Trials += 1;
            b.RanOver = 1;
        }
        Assert.Equal(EarlyStop.ShouldStop(plain, 0.05), EarlyStop.ShouldStop(ranOver, 0.05));
    }

    [Fact]
    public void EarlyStopNeverStopsAtARateOfZero() => Assert.False(EarlyStop.ShouldStop(Steady(30, _ => 0), 0.5));

    [Fact]
    public void EarlyStopComparesTheHalfWidthWithThePrecision()
    {
        // Kills swing between 20 and 40 per batch: the 95% half-width of the rate over 10 batches is about ±22%.
        var noisy = Steady(10, b => b % 2 == 0 ? 20 : 40);
        var estimate = Estimators.RatioOfBatches(
            [.. noisy.Select(t => (double)t.Kills)],
            [.. noisy.Select(t => t.GameSeconds)]
        );
        var relative = (estimate.High - estimate.Point) / estimate.Point;
        Assert.InRange(relative, 0.2, 0.24);
        Assert.False(EarlyStop.ShouldStop(noisy, 0.2));
        Assert.True(EarlyStop.ShouldStop(noisy, 0.25));
    }
}
