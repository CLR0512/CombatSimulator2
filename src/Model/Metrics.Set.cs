namespace MelvorMods.CombatSimulator2.Model;

public static partial class Metrics
{
    // A set's figure: the mean over its parts of each part's rate times its weight (one over current gear's rate on
    // it), so with current gear's weights it is the mean of each part's gain. Batch i of the set is each part's rate
    // plus batch i's residual from it over the part's mean batch time (the delta method the ratio of batch sums is
    // ranged by), weighted and averaged over the parts, over a time of one: the batches' mean is the figure exactly, and
    // their spread gives each part its own range, paired by batch as every other rate is. Parts weighted 0 and parts
    // without figures count for nothing; null with no part left, an extreme or a death rate, or before the weights are
    // known.
    private static (double[] Values, double[] Denominators)? SetSeries(
        TargetResult set,
        string key,
        TimeUnit unit,
        PriceTable? prices
    )
    {
        if (
            set is not { State: TargetState.Finished, Parts: { } parts, Weights: { } weights }
            || weights.Count != parts.Count
            || key is DeathRate or HighestHit or LowestHp or HighestReflect
        )
            return null;
        var counted = new List<(double Weight, double[] Values, double[] Times)>();
        for (var i = 0; i < parts.Count; i++)
        {
            if (weights[i] <= 0 || !parts[i].HasFigures || Series(parts[i], key, unit, prices) is not { } series)
                continue;
            if (series.Denominators.Sum() > 0)
                counted.Add((weights[i], series.Values, series.Denominators));
        }
        if (counted.Count == 0)
            return null;
        var batches = counted.Min(c => c.Values.Length);
        var values = new double[batches];
        foreach (var (weight, partValues, times) in counted)
        {
            var rate = partValues.Take(batches).Sum() / times.Take(batches).Sum();
            var meanTime = times.Take(batches).Average();
            for (var b = 0; b < batches; b++)
                values[b] += weight * (rate + (partValues[b] - rate * times[b]) / meanTime) / counted.Count;
        }
        return (values, Enumerable.Repeat(1.0, batches).ToArray());
    }
}
