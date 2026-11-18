namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>
/// A master's task in the save: its monster (an enemy id), kills done of its length, whether it was extended (the
/// game allows once), and what extending (the master's base task length more kills) and rerolling (a new monster drawn
/// from the pool) cost in the master's coin, modifiers included.
/// </summary>
public sealed record TaskNow(
    string Master,
    string Enemy,
    int Progress,
    int Length,
    bool Extended,
    int BaseLength,
    long RerollCost,
    long ExtensionCost,
    string Currency
)
{
    public int Left => Math.Max(0, Length - Progress);
}

public enum TaskAdvice
{
    Keep,
    Extend,
    Reroll,
}

/// <summary>
/// What to do with a task: its monster's rate on task against the master's average, the kills the advice is about
/// (the base length to extend by, or the kills left to reroll away), the gain over them in the metric (null when
/// keeping), the hours those kills take, and what it costs; when the metric is the master's own coin, an action is advised only when it earns more
/// than it costs.
/// </summary>
public sealed record TaskVerdict(
    TaskAdvice Advice,
    Estimate Rate,
    Estimate Average,
    int Kills,
    double? Gain,
    long Cost,
    bool SameCoin = false,
    double Hours = 0
)
{
    /// <summary>The gain less the cost, when the metric is the coin paid: then the two are one unit.</summary>
    public double? Net => SameCoin && Gain is { } gain ? gain - Cost : null;

    /// <summary>The task's rate over the master's.</summary>
    public double Ratio => Average.Point == 0 ? double.NaN : Rate.Point / Average.Point;
}

/// <summary>
/// Tasks judged against their master from a run of the master. A rerolled task is drawn evenly from the pool as every
/// task is, so the kills left would go at the master's average instead; an extension adds kills of this monster where
/// the next task would go at the average. Either is advised only when the two ranges don't overlap. Godot-free.
/// </summary>
public static class TaskVerdicts
{
    /// <summary>How far off its master's rate a task must be for any advice: below it, the task reads as about it.</summary>
    public const double Material = 0.05;

    /// <summary>
    /// The verdict on <paramref name="task"/> from <paramref name="master"/>'s result with its parts, by
    /// <paramref name="metric"/> per hour; null when the run has no figure for the master or the task's monster.
    /// </summary>
    public static TaskVerdict? For(TaskNow task, TargetResult master, string metric, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(master);
        var parts = MasterParts.Counted(master);
        // The task's monster among all the master's: a setup may block it, which leaves it out of the master's rate
        // but not out of the task the save holds.
        var part = master.Parts?.Find(p => EnemyOf(p.Key) == task.Enemy);
        if (part == null || MasterFigures.Estimate(parts, metric, TimeUnit.Hour, prices) is not { } average)
            return null;
        if (Metrics.Estimate(part, metric, TimeUnit.Hour, prices) is not { } rate)
            return null;
        var kills = part.Batches.Sum(b => b.Kills);
        if (kills == 0)
            return null;
        var hoursPerKill = part.Batches.Sum(b => b.GameSeconds) / kills / 3600;
        var sameCoin = metric == "currency:" + task.Currency;
        TaskVerdict? advised = null;
        // Advised only when the ranges don't overlap and the task is at least 5% off: a sure but small difference isn't
        // worth paying for, and the panel reads it as about the master's rate.
        var apart = Math.Abs(rate.Point / average.Point - 1) >= Material;
        if (apart && rate.Low > average.High && !task.Extended)
            advised = new TaskVerdict(
                TaskAdvice.Extend,
                rate,
                average,
                task.BaseLength,
                (rate.Point - average.Point) * task.BaseLength * hoursPerKill,
                task.ExtensionCost,
                sameCoin,
                task.BaseLength * hoursPerKill
            );
        else if (apart && rate.High < average.Low && task.Left > 0)
            advised = new TaskVerdict(
                TaskAdvice.Reroll,
                rate,
                average,
                task.Left,
                (average.Point - rate.Point) * task.Left * hoursPerKill,
                task.RerollCost,
                sameCoin,
                task.Left * hoursPerKill
            );
        return advised is { Net: not <= 0 }
            ? advised
            : new TaskVerdict(TaskAdvice.Keep, rate, average, task.Left, null, 0, sameCoin);
    }

    // A part key's enemy id: "part:<master>|<area>/<enemy>" ends with the enemy.
    private static string EnemyOf(string partKey)
    {
        var target = Ids.PartTarget(partKey);
        return target[(target.LastIndexOf('/') + 1)..];
    }
}
