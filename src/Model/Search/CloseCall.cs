namespace MelvorMods.CombatSimulator2.Model.Search;

public enum CloseCallOutcome
{
    /// <summary>The longer race kept the step's winner.</summary>
    Kept,

    /// <summary>The runner-up won the longer race and took its place.</summary>
    Swapped,

    /// <summary>Still too close: the setup's own option is kept, else the one owned more.</summary>
    Tie,
}

/// <summary>
/// Two options for one slot or choice too close to call in their step, raced again in the finished setup:
/// <see cref="Subject"/> names the slot or kind, <see cref="Kept"/> and <see cref="RunnerUp"/> the options by name
/// (with their sets' changes), <see cref="Difference"/> the runner-up's difference from the kept one, relative to it,
/// with its 95% range.
/// </summary>
public sealed class CloseCall
{
    public string Subject { get; set; } = "";
    public string Kept { get; set; } = "";
    public ChangeList KeptChanges { get; set; } = ChangeList.Empty;
    public string RunnerUp { get; set; } = "";

    /// <summary>The kept and the runner-up options' game ids, for their icons.</summary>
    public List<string> KeptIds { get; set; } = [];
    public List<string> RunnerUpIds { get; set; } = [];
    public ChangeList RunnerUpChanges { get; set; } = ChangeList.Empty;
    public Estimate? Difference { get; set; }
    public int Trials { get; set; }
    public ulong Seed { get; set; }
    public CloseCallOutcome Outcome { get; set; }
}

/// <summary>How a close call is found in a step and settled by its longer race.</summary>
public static class CloseCalls
{
    /// <summary>
    /// Whether a tie between two sets of one step is a close call: they differ in one slot or one choice only, so
    /// a longer race in the finished setup can tell them apart.
    /// </summary>
    public static bool IsCloseCall(ChangeList a, ChangeList b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var keys = a.Select(c => c.Key).Union(b.Select(c => c.Key)).Where(k => a.Find(k)?.Value != b.Find(k)?.Value);
        // A pair of prayers is one choice, though each prayer is a change of its own.
        return keys.Select(k => k.Kind == ChangeKind.Prayer ? new ChangeKey(ChangeKind.Prayer, null) : k)
                .Distinct()
                .Count() == 1;
    }

    /// <summary>
    /// How many of the items <paramref name="set"/> puts in the close call's <paramref name="keys"/> the character
    /// has, by <paramref name="owned"/>: the fewest of them, so a pair counts as its scarcer piece; 0 when it puts no
    /// item there.
    /// </summary>
    public static int Owned(ChangeList set, IEnumerable<ChangeKey> keys, Func<string, int> owned)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(owned);
        var counts = keys.Select(set.Find)
            .Where(c => c?.Kind is ChangeKind.Equip or ChangeKind.Toolbelt or ChangeKind.Food)
            .Select(c => owned(c!.Value))
            .ToList();
        return counts.Count == 0 ? 0 : counts.Min();
    }

    /// <summary>
    /// Settles a close call from <paramref name="difference"/>, the runner-up's value less the kept one's over the
    /// longer race with its 95% range: a clear win either way decides; else it is a tie, which keeps the setup's own
    /// option, else the one the character owns more of. True in the second value when the runner-up takes the place.
    /// </summary>
    public static (CloseCallOutcome Outcome, bool RunnerUp) Settle(
        Estimate difference,
        bool lowerIsBetter,
        bool keptIsOwn,
        bool runnerUpIsOwn,
        int keptOwned,
        int runnerUpOwned
    )
    {
        ArgumentNullException.ThrowIfNull(difference);
        var (low, high) = lowerIsBetter ? (-difference.High, -difference.Low) : (difference.Low, difference.High);
        if (low > 0)
            return (CloseCallOutcome.Swapped, true);
        if (high < 0)
            return (CloseCallOutcome.Kept, false);
        if (keptIsOwn || runnerUpIsOwn)
            return (CloseCallOutcome.Tie, runnerUpIsOwn);
        return (CloseCallOutcome.Tie, runnerUpOwned > keptOwned);
    }
}
