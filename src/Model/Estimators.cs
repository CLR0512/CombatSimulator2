namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A point estimate with its 95% interval.</summary>
/// <param name="Method"><c>ratio</c> (ratio of batch sums) or <c>wilson</c>.</param>
public sealed record Estimate(double Point, double Low, double High, int N, string Method)
{
    /// <summary>The standard error implied by the interval (it is ±1.96 SE wide).</summary>
    public double StandardError => double.IsFinite(High - Low) ? (High - Low) / (2 * Estimators.Z95) : double.NaN;

    /// <summary>The estimate scaled by a positive factor, interval included.</summary>
    public Estimate Scale(double factor) =>
        this with
        {
            Point = Point * factor,
            Low = Low * factor,
            High = High * factor,
        };
}

/// <summary>Godot-free statistics for simulation results.</summary>
public static class Estimators
{
    public const double Z95 = 1.959963984540054;

    /// <summary>The Wilson score interval for a proportion, which stays sensible at 0 and 1.</summary>
    public static Estimate Wilson(int successes, int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfLessThan(n, successes);
        if (n == 0)
            return new Estimate(0, 0, 1, 0, "wilson");
        var p = successes / (double)n;
        const double z2 = Z95 * Z95;
        var denominator = 1 + z2 / n;
        var centre = (p + z2 / (2.0 * n)) / denominator;
        var half = Z95 * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denominator;
        // Clamped so rounding at 0 and 1 never puts the point outside its own interval.
        var low = Math.Clamp(centre - half, 0, p);
        var high = Math.Clamp(centre + half, p, 1);
        return new Estimate(p, low, high, n, "wilson");
    }

    /// <summary>
    /// The ratio of batch sums, R = Σv / Σt, with a delta-method 95% interval on dᵢ = vᵢ − R·tᵢ:
    /// SE(R) = s_d / (√B · t̄). When every value is non-negative the low end is clamped at 0.
    /// </summary>
    public static Estimate RatioOfBatches(IReadOnlyList<double> values, IReadOnlyList<double> times)
    {
        var (ratio, sd, meanTime, batches) = Ratio(values, times);
        if (batches == 0 || meanTime <= 0)
            return new Estimate(0, 0, 0, batches, "ratio");
        var se = batches > 1 ? sd / (Math.Sqrt(batches) * meanTime) : double.PositiveInfinity;
        var low = ratio - Z95 * se;
        if (values.All(v => v >= 0))
            low = Math.Max(0, low);
        return new Estimate(ratio, low, ratio + Z95 * se, batches, "ratio");
    }

    /// <summary>
    /// The trials needed for a 95% interval of ±<paramref name="relativePrecision"/> around R: batches
    /// n = (1.96 · s_d / (e · R · t̄))², times <paramref name="trialsPerBatch"/>. int.MaxValue when R is 0
    /// or there are fewer than 2 batches.
    /// </summary>
    public static int TrialsFor(
        double relativePrecision,
        IReadOnlyList<double> values,
        IReadOnlyList<double> times,
        double trialsPerBatch
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(relativePrecision);
        var (ratio, sd, meanTime, batches) = Ratio(values, times);
        if (batches < 2 || ratio <= 0 || meanTime <= 0)
            return int.MaxValue;
        var neededBatches = Math.Pow(Z95 * sd / (relativePrecision * ratio * meanTime), 2);
        var trials = Math.Ceiling(Math.Max(1, neededBatches) * trialsPerBatch);
        return trials >= int.MaxValue ? int.MaxValue : (int)trials;
    }

    /// <summary>The two-sample z score: (obs − sim) / √(seObs² + seSim²).</summary>
    public static double Z(double observed, double observedSe, double simulated, double simulatedSe)
    {
        var se = Math.Sqrt(observedSe * observedSe + simulatedSe * simulatedSe);
        if (se > 0)
            return (observed - simulated) / se;
        return observed == simulated ? 0 : double.PositiveInfinity;
    }

    private static (double Ratio, double Sd, double MeanTime, int Batches) Ratio(
        IReadOnlyList<double> values,
        IReadOnlyList<double> times
    )
    {
        if (values.Count != times.Count)
            throw new ArgumentException("values and times differ in length", nameof(times));
        var b = values.Count;
        if (b == 0)
            return (0, 0, 0, 0);
        double sumV = 0,
            sumT = 0;
        for (var i = 0; i < b; i++)
        {
            sumV += values[i];
            sumT += times[i];
        }
        if (sumT <= 0)
            return (0, 0, 0, b);
        var ratio = sumV / sumT;
        var ss = 0.0;
        for (var i = 0; i < b; i++)
        {
            var d = values[i] - ratio * times[i];
            ss += d * d;
        }
        return (ratio, b > 1 ? Math.Sqrt(ss / (b - 1)) : 0, sumT / b, b);
    }
}

/// <summary>
/// When a target may stop before its full trials: once its kills rate (completions for dungeons) is known well
/// enough.
/// </summary>
public static class EarlyStop
{
    /// <summary>The complete batches needed before stopping is considered.</summary>
    private const int MinBatches = 10;

    /// <summary>
    /// True when at least <see cref="MinBatches"/> batches are complete and the 95% half-width of the kills rate,
    /// relative to the rate, is within <paramref name="precision"/> (0.05 = ±5%). Never at a rate of 0.
    /// </summary>
    public static bool ShouldStop(IReadOnlyList<Tally> completeBatches, double precision)
    {
        ArgumentNullException.ThrowIfNull(completeBatches);
        if (completeBatches.Count < MinBatches)
            return false;
        var estimate = Estimators.RatioOfBatches(
            [.. completeBatches.Select(b => (double)b.Kills)],
            [.. completeBatches.Select(b => b.GameSeconds)]
        );
        if (estimate.Point <= 0 || !double.IsFinite(estimate.High))
            return false;
        return (estimate.High - estimate.Point) / estimate.Point <= precision;
    }
}

/// <summary>
/// Groups consecutive trials into batches. Trials aren't independent (HP carries over from one to the next), and
/// batch sums absorb that.
/// </summary>
public static class Batches
{
    public const int Default = 30;

    /// <summary>The batches for <paramref name="trials"/> trials: 30, or one per trial when there are fewer.</summary>
    public static int Count(int trials) => Math.Clamp(trials, 0, Default);

    /// <summary>
    /// The batch of trial <paramref name="trial"/> (0-based) out of <paramref name="trials"/>, as even as possible.
    /// </summary>
    public static int IndexOf(int trial, int trials)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trial);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(trial, trials);
        return (int)((long)trial * Count(trials) / trials);
    }
}
