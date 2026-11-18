namespace MelvorMods.CombatSimulator2.Model;

/// <summary>What a difference between two setups says, judged on its whole 95% range.</summary>
public enum Verdict
{
    /// <summary>No difference to judge: a side has no estimate, or the sides measure different things.</summary>
    None,

    /// <summary>The range includes 0.</summary>
    NoClearDifference,

    /// <summary>The whole range is on B's better side, or only B can kill the target in time.</summary>
    Better,

    /// <summary>The whole range is on B's worse side, or only A can kill the target in time.</summary>
    Worse,
}

/// <summary>How the range of a difference is worked out.</summary>
public enum DifferenceMethod
{
    /// <summary>The two sides treated as independent samples.</summary>
    Independent,

    /// <summary>Batch i of one side paired with batch i of the other (both start from the same seed).</summary>
    Paired,
}

/// <summary>One target's comparison by one metric: A is the baseline, the difference is B − A.</summary>
/// <param name="Delta">
/// B − A with its 95% range; null without an estimate on both sides, or with an unbounded side.
/// </param>
/// <param name="Percent">B / A − 1 with its range; null unless both points are above 0 and finite.</param>
/// <param name="SkillsDiffer">Whether "XP · main skill" stands for a different skill on each side.</param>
/// <param name="ReasonsA">Why A has no estimate (refused, cancelled, running…); null with one.</param>
/// <param name="DiedA">Every trial of A died. With either side so, there is no verdict: a value earned while dying
/// is neither better nor worse than one earned alive.</param>
/// <param name="StallA">A's trial ran over with none finished: its reason; null otherwise.</param>
/// <param name="RanOverA">A finished with trials that ran over: how many, of how many; null otherwise.</param>
public sealed record Difference(
    string Key,
    string Name,
    Estimate? A,
    Estimate? B,
    Estimate? Delta,
    Estimate? Percent,
    Verdict Verdict,
    bool LowerIsBetter,
    bool SkillsDiffer,
    TargetState StateA,
    TargetState StateB,
    IReadOnlyList<Note>? ReasonsA,
    IReadOnlyList<Note>? ReasonsB,
    bool DiedA = false,
    bool DiedB = false,
    StallMark? StallA = null,
    StallMark? StallB = null,
    RanOverCount? RanOverA = null,
    RanOverCount? RanOverB = null
)
{
    /// <summary>Why there is no verdict when a side died every trial; null otherwise.</summary>
    public Note? Reason =>
        (DiedA, DiedB) switch
        {
            (true, true) => Note.Of("Both died every fight"),
            (true, false) => Note.Of("A died every fight"),
            (false, true) => Note.Of("B died every fight"),
            _ => null,
        };

    /// <summary>
    /// The side that alone finished fights, "A" or "B", when the other's fights all ran over the limit with none
    /// finished (it couldn't kill the target in time); null otherwise.
    /// </summary>
    public string? OnlyKills =>
        StallA != null && A == null && B != null ? "B"
        : StallB != null && B == null && A != null ? "A"
        : null;

    /// <summary>How much better B is, signed so that above 0 is always better (NaN without a difference).</summary>
    public double Improvement =>
        Delta == null ? double.NaN
        : LowerIsBetter ? -Delta.Point
        : Delta.Point;
}

/// <summary>A side's trials that ran over the limit, of all its trials.</summary>
public sealed record RanOverCount(int RanOver, int Trials)
{
    public static RanOverCount? Of(TargetResult? result) =>
        result is { HasEstimates: true, RanOver: > 0 }
            ? new RanOverCount(result.RanOver, result.Batches.Sum(b => b.Trials))
            : null;
}

/// <summary>Differences between two runs of the same targets, with ranges and verdicts. Godot-free.</summary>
public static class Differences
{
    /// <summary>
    /// The method comparisons use. Paired: both sides of a target start from the same seed, so their batches move
    /// together and pairing them gives ranges about a third narrower; it falls back to independent where the batch
    /// counts differ (early stop) or the metric is a proportion.
    /// </summary>
    public static DifferenceMethod Method { get; set; } = DifferenceMethod.Paired;

    /// <summary>The difference B − A with a 95% range from both standard errors: d ± 1.96·√(SE_A² + SE_B²).</summary>
    public static Estimate Independent(Estimate a, Estimate b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var d = b.Point - a.Point;
        var seA = a.StandardError;
        var seB = b.StandardError;
        var se = Math.Sqrt(seA * seA + seB * seB);
        if (double.IsNaN(se))
            se = double.PositiveInfinity;
        return new Estimate(d, d - Estimators.Z95 * se, d + Estimators.Z95 * se, Math.Min(a.N, b.N), "independent");
    }

    /// <summary>
    /// The difference B − A for two ratios of batch sums, batch i of each side paired: Var(R_B − R_A) = Var(R_B) +
    /// Var(R_A) − 2·Cov, the covariance taken from the two sides' batch residuals. Null when the batch counts differ
    /// or are below 2, or a side's time is 0.
    /// </summary>
    public static Estimate? Paired(
        IReadOnlyList<double> valuesA,
        IReadOnlyList<double> timesA,
        IReadOnlyList<double> valuesB,
        IReadOnlyList<double> timesB
    )
    {
        ArgumentNullException.ThrowIfNull(valuesA);
        ArgumentNullException.ThrowIfNull(timesA);
        ArgumentNullException.ThrowIfNull(valuesB);
        ArgumentNullException.ThrowIfNull(timesB);
        var n = valuesA.Count;
        if (n < 2 || timesA.Count != n || valuesB.Count != n || timesB.Count != n)
            return null;
        var (ra, meanTa) = RatioOf(valuesA, timesA);
        var (rb, meanTb) = RatioOf(valuesB, timesB);
        if (meanTa <= 0 || meanTb <= 0)
            return null;
        double ssA = 0,
            ssB = 0,
            sAB = 0;
        for (var i = 0; i < n; i++)
        {
            var da = valuesA[i] - ra * timesA[i];
            var db = valuesB[i] - rb * timesB[i];
            ssA += da * da;
            ssB += db * db;
            sAB += da * db;
        }
        var varA = ssA / (n - 1) / (n * meanTa * meanTa);
        var varB = ssB / (n - 1) / (n * meanTb * meanTb);
        var cov = sAB / (n - 1) / (n * meanTa * meanTb);
        var se = Math.Sqrt(Math.Max(0, varA + varB - 2 * cov));
        var d = rb - ra;
        return new Estimate(d, d - Estimators.Z95 * se, d + Estimators.Z95 * se, n, "paired");
    }

    /// <summary>
    /// The difference B − A for two proportions, from each side's Wilson interval (Newcombe's hybrid score
    /// interval), so the range stays sensible at 0 and 1.
    /// </summary>
    public static Estimate Newcombe(Estimate a, Estimate b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var d = b.Point - a.Point;
        var low = d - Math.Sqrt(Square(b.Point - b.Low) + Square(a.High - a.Point));
        var high = d + Math.Sqrt(Square(b.High - b.Point) + Square(a.Point - a.Low));
        return new Estimate(d, low, high, Math.Min(a.N, b.N), "newcombe");
    }

    /// <summary>
    /// The relative difference B / A − 1 with its range; null unless both points are above 0 and finite. With the
    /// difference <paramref name="delta"/>, the range is the difference's range relative to A, so the percentage and
    /// the verdict always agree on whether 0 is in it; without one, a log-ratio range,
    /// ln(B/A) ± 1.96·√((SE_A/A)² + (SE_B/B)²). Never below −1.
    /// </summary>
    public static Estimate? Percent(Estimate a, Estimate b, Estimate? delta = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!(a.Point > 0) || !(b.Point > 0) || !double.IsFinite(a.Point) || !double.IsFinite(b.Point))
            return null;
        var point = b.Point / a.Point - 1;
        if (delta != null)
            return new Estimate(point, Math.Max(-1, delta.Low / a.Point), delta.High / a.Point, delta.N, "relative");
        var log = Math.Log(b.Point / a.Point);
        var relA = a.StandardError / a.Point;
        var relB = b.StandardError / b.Point;
        var se = Math.Sqrt(relA * relA + relB * relB);
        if (double.IsNaN(se))
            se = double.PositiveInfinity;
        return new Estimate(
            point,
            Math.Exp(log - Estimators.Z95 * se) - 1,
            Math.Exp(log + Estimators.Z95 * se) - 1,
            Math.Min(a.N, b.N),
            "log-ratio"
        );
    }

    /// <summary>
    /// The verdict on <paramref name="delta"/>: better or worse only when the whole range is on one side of 0, lower
    /// being better when <paramref name="lowerIsBetter"/>.
    /// </summary>
    public static Verdict VerdictOf(Estimate? delta, bool lowerIsBetter)
    {
        if (delta == null || double.IsNaN(delta.Low) || double.IsNaN(delta.High))
            return Verdict.None;
        if (delta.Low > 0)
            return lowerIsBetter ? Verdict.Worse : Verdict.Better;
        if (delta.High < 0)
            return lowerIsBetter ? Verdict.Better : Verdict.Worse;
        return Verdict.NoClearDifference;
    }

    /// <summary>
    /// Every target of <paramref name="a"/> compared with the same target in <paramref name="b"/>, in A's target
    /// order. <c>xp:main</c> is resolved on each side; when the skills differ the difference is still shown, with
    /// no verdict.
    /// </summary>
    public static IReadOnlyList<Difference> Of(
        Run a,
        Run b,
        string metric,
        TimeUnit unit,
        PriceTable? prices = null,
        DifferenceMethod? method = null
    )
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(metric);
        var keyA = Metrics.Resolve(a, metric);
        var keyB = Metrics.Resolve(b, metric);
        var skillsDiffer = !string.Equals(keyA, keyB, StringComparison.Ordinal);
        var lower = Ranking.LowerIsBetter(keyA);
        var list = new List<Difference>(a.Targets.Count);
        foreach (var target in a.Targets.Distinct(StringComparer.Ordinal))
        {
            var ra = a.Results.GetValueOrDefault(target);
            var rb = b.Results.GetValueOrDefault(target);
            list.Add(Of(target, ra, rb, keyA, keyB, unit, prices, method ?? Method, lower, skillsDiffer));
        }
        return list;
    }

    /// <summary>
    /// One target compared on each of <paramref name="metrics"/>, in that order: the details panel's rows.
    /// <c>xp:main</c> is resolved on each side as in
    /// <see cref="Of(Run, Run, string, TimeUnit, PriceTable?, DifferenceMethod?)"/>.
    /// </summary>
    public static IReadOnlyList<Difference> ForTarget(
        Run a,
        Run b,
        string target,
        IEnumerable<string> metrics,
        TimeUnit unit,
        PriceTable? prices = null,
        DifferenceMethod? method = null
    )
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(metrics);
        var ra = a.Results.GetValueOrDefault(target);
        var rb = b.Results.GetValueOrDefault(target);
        var list = new List<Difference>();
        foreach (var metric in metrics)
        {
            var keyA = Metrics.Resolve(a, metric);
            var keyB = Metrics.Resolve(b, metric);
            list.Add(
                Of(
                    target,
                    ra,
                    rb,
                    keyA,
                    keyB,
                    unit,
                    prices,
                    method ?? Method,
                    Ranking.LowerIsBetter(keyA),
                    !string.Equals(keyA, keyB, StringComparison.Ordinal)
                )
            );
        }
        return list;
    }

    private static Difference Of(
        string target,
        TargetResult? ra,
        TargetResult? rb,
        string keyA,
        string keyB,
        TimeUnit unit,
        PriceTable? prices,
        DifferenceMethod method,
        bool lower,
        bool skillsDiffer
    )
    {
        var name =
            ra?.Name is { Length: > 0 } n ? Spoilers.Current.Name(n)
            : rb?.Name is { Length: > 0 } m ? Spoilers.Current.Name(m)
            : target;
        var stateA = ra?.State ?? TargetState.Queued;
        var stateB = rb?.State ?? TargetState.Queued;
        var ea = Usable(ra == null ? null : Metrics.Estimate(ra, keyA, unit, prices));
        var eb = Usable(rb == null ? null : Metrics.Estimate(rb, keyB, unit, prices));
        Estimate? delta = null;
        Estimate? percent = null;
        if (ea != null && eb != null)
        {
            if (double.IsFinite(ea.Point) && double.IsFinite(eb.Point))
            {
                delta = Delta(ra!, rb!, ea, eb, keyA, keyB, unit, prices, method);
                percent = Percent(ea, eb, delta);
            }
        }
        var diedA = ra?.NoneSurvived == true;
        var diedB = rb?.NoneSurvived == true;
        var stallA = StallGroups.MarkOf(ra);
        var stallB = StallGroups.MarkOf(rb);
        // A side that can kill the target beats one that can't in time, whatever the metric: the other has no rate.
        var verdict =
            skillsDiffer || diedA || diedB ? Verdict.None
            : stallA != null && ea == null && eb != null ? Verdict.Better
            : stallB != null && eb == null && ea != null ? Verdict.Worse
            : VerdictOf(delta, lower);
        return new Difference(
            target,
            name,
            ea,
            eb,
            delta,
            percent,
            verdict,
            lower,
            skillsDiffer,
            stateA,
            stateB,
            ea == null ? Ranking.ReasonOf(ra, stateA) : null,
            eb == null ? Ranking.ReasonOf(rb, stateB) : null,
            diedA,
            diedB,
            stallA,
            stallB,
            RanOverCount.Of(ra),
            RanOverCount.Of(rb)
        );
    }

    private static Estimate Delta(
        TargetResult ra,
        TargetResult rb,
        Estimate ea,
        Estimate eb,
        string keyA,
        string keyB,
        TimeUnit unit,
        PriceTable? prices,
        DifferenceMethod method
    )
    {
        if (keyA == Metrics.DeathRate)
            return Newcombe(ea, eb);
        if (
            method == DifferenceMethod.Paired
            && Metrics.Series(ra, keyA, unit, prices) is { } sa
            && Metrics.Series(rb, keyB, unit, prices) is { } sb
            && Paired(sa.Values, sa.Denominators, sb.Values, sb.Denominators) is { } paired
        )
            return paired;
        return Independent(ea, eb);
    }

    private static Estimate? Usable(Estimate? estimate) =>
        estimate == null || double.IsNaN(estimate.Point) ? null : estimate;

    private static (double Ratio, double MeanTime) RatioOf(IReadOnlyList<double> values, IReadOnlyList<double> times)
    {
        double sumV = 0,
            sumT = 0;
        for (var i = 0; i < values.Count; i++)
        {
            sumV += values[i];
            sumT += times[i];
        }
        return sumT <= 0 ? (0, 0) : (sumV / sumT, sumT / values.Count);
    }

    private static double Square(double x) => x * x;
}
