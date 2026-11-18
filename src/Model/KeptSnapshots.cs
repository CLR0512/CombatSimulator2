namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A failed run's or search's snapshot on disk, by its run or search id.</summary>
public sealed record SnapshotFile(string Id, DateTime TakenAt, long Bytes);

/// <summary>
/// Which failed runs' snapshots stay on disk: the newest few, within the space crash files may take together with the
/// host's dumps; the oldest go first.
/// </summary>
public static class KeptSnapshots
{
    public const int KeepCount = 5;

    /// <summary>The ids to remove, oldest first.</summary>
    public static IReadOnlyList<string> ToRemove(
        IEnumerable<SnapshotFile> snapshots,
        long dumpBytes,
        long keepBytes,
        int keepCount = KeepCount
    )
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var total = dumpBytes;
        var kept = 0;
        var removed = new List<string>();
        foreach (var snapshot in snapshots.OrderByDescending(s => s.TakenAt).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            if (kept < keepCount && total + snapshot.Bytes <= keepBytes)
            {
                kept++;
                total += snapshot.Bytes;
            }
            else
                removed.Add(snapshot.Id);
        }
        removed.Reverse();
        return removed;
    }
}
