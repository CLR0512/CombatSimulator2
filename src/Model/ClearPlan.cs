namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What "Clear saved runs" will do, worked out once so that the counts shown and the files deleted are the same
/// thing. Pinned runs stay; a kept comparison stays only when both its runs do.
/// </summary>
/// <param name="Delete">The runs to delete: not pinned and not in progress.</param>
/// <param name="PinnedKept">How many pinned runs stay.</param>
/// <param name="ComparisonsDelete">The kept comparisons that lose a run, by id.</param>
/// <param name="ComparisonsKept">How many kept comparisons stay, both their runs being pinned.</param>
public sealed record ClearPlan(
    IReadOnlyList<string> Delete,
    int PinnedKept,
    IReadOnlyList<string> ComparisonsDelete,
    int ComparisonsKept
)
{
    /// <summary>Whether there is no run and no comparison to delete.</summary>
    public bool Nothing => Delete.Count == 0 && ComparisonsDelete.Count == 0;

    /// <summary>Whether the plan deletes the run with that id.</summary>
    public bool Deletes(string runId) => Delete.Contains(runId, StringComparer.Ordinal);

    /// <summary>
    /// The plan for the saved runs <paramref name="headers"/> and the kept comparisons. A run in
    /// <paramref name="inProgress"/>, and a comparison that names one, is being made right now: it is neither
    /// deleted nor counted.
    /// </summary>
    public static ClearPlan Of(
        IEnumerable<RunHeader> headers,
        IEnumerable<Comparison> comparisons,
        IReadOnlySet<string> inProgress
    )
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(comparisons);
        ArgumentNullException.ThrowIfNull(inProgress);
        var saved = headers.Where(h => !inProgress.Contains(h.Id)).ToList();
        var pinned = saved.Where(h => h.Pinned).Select(h => h.Id).ToHashSet(StringComparer.Ordinal);
        var settled = comparisons.Where(c => !inProgress.Contains(c.A) && !inProgress.Contains(c.B)).ToList();
        var stay = settled.Where(c => pinned.Contains(c.A) && pinned.Contains(c.B)).ToList();
        return new ClearPlan(
            [.. saved.Where(h => !h.Pinned).Select(h => h.Id)],
            pinned.Count,
            [.. settled.Except(stay).Select(c => c.Id)],
            stay.Count
        );
    }
}
