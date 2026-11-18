using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// The comparisons CS2 keeps and their file: a finished comparison joins them, the oldest of more than
/// <see cref="KeptComparisons.Max"/> leaves, and any of them can be opened again from its two saved runs. Changed
/// on the main thread; the run store reads the protected runs from the thread that saves a run.
/// </summary>
internal sealed class ComparisonKeeper
{
    private readonly RunStore _store;
    private readonly FailureLog _failures;
    private readonly Lock _lock = new();
    private readonly Lock _writing = new();
    private readonly KeptComparisons _kept;

    // The entries as last listed, with the store's change count and which of their runs were on disk then; null
    // after any change here.
    private (long StoreChanges, string OnDisk, IReadOnlyList<KeptComparison> Entries)? _listed;

    public ComparisonKeeper(RunStore store, FailureLog failures)
    {
        _store = store;
        _failures = failures;
        var (records, problem) = ComparisonsFile.Read(store.Folder);
        if (problem != null)
            Log.Warn("kept comparisons not read, starting with none: " + problem);
        _kept = new KeptComparisons(records);
    }

    /// <summary>The kept records, newest first.</summary>
    public IReadOnlyList<Comparison> Records
    {
        get
        {
            lock (_lock)
                return [.. _kept.Entries];
        }
    }

    /// <summary>The runs of every kept comparison: they stay past the keep limit.</summary>
    public IReadOnlySet<string> RunIds
    {
        get
        {
            lock (_lock)
                return _kept.RunIds;
        }
    }

    /// <summary>The runs of every kept comparison and of <paramref name="shown"/>, the one on screen.</summary>
    public IReadOnlySet<string> RunsWith(ComparedRuns? shown)
    {
        var ids = new HashSet<string>(RunIds, StringComparer.Ordinal);
        if (shown != null)
            ids.UnionWith(new[] { shown.A.Id, shown.B.Id }.Where(id => id.Length > 0));
        return ids;
    }

    /// <summary>Each kept comparison with its runs' headers, newest first; a run that is gone has none.</summary>
    public IReadOnlyList<KeptComparison> Entries()
    {
        // Listing reads every run's header: once per change, since a list on screen asks every refresh. A run file
        // taken away by hand changes nothing the store counts, so the kept runs are looked for each time.
        var changes = _store.Changes;
        var onDisk = string.Concat(
            Records.SelectMany(r => new[] { r.A, r.B }).Select(id => _store.Has(id) ? '+' : '-')
        );
        lock (_lock)
        {
            if (_listed is { } listed && listed.StoreChanges == changes && listed.OnDisk == onDisk)
                return listed.Entries;
        }
        var headers = _store.List().ToDictionary(h => h.Id, StringComparer.Ordinal);
        IReadOnlyList<KeptComparison> entries =
        [
            .. Records.Select(r => new KeptComparison(
                r,
                headers.GetValueOrDefault(r.A),
                headers.GetValueOrDefault(r.B),
                _store.CharacterOf(r.B) ?? _store.CharacterOf(r.A)
            )),
        ];
        lock (_lock)
            _listed = (changes, onDisk, entries);
        return entries;
    }

    /// <summary>
    /// Keeps a comparison that became final, when side B ran. Returns the record that left to make room, whose runs
    /// are ordinary saved runs again, or null.
    /// </summary>
    public Comparison? Keep(ComparedRuns comparison)
    {
        if (!comparison.BStarted || comparison.A.Id.Length == 0)
            return null;
        Comparison? dropped;
        lock (_lock)
        {
            dropped = _kept.Add(comparison.Record);
            _listed = null;
        }
        Save(comparison.Record.Id);
        if (dropped != null)
            Log.Info($"{dropped.Id} no longer kept: {KeptComparisons.Max} newer comparisons");
        return dropped;
    }

    /// <summary>Stops keeping the comparisons with these ids; returns how many there were.</summary>
    public int Remove(IEnumerable<string> ids)
    {
        var removed = 0;
        lock (_lock)
        {
            foreach (var id in ids)
            {
                if (_kept.Remove(id))
                    removed++;
            }
            _listed = null;
        }
        if (removed > 0)
            Save("comparisons");
        return removed;
    }

    /// <summary>
    /// A kept comparison to show again as it finished. Refused, with what to do, for an id that isn't kept and for
    /// a comparison with a run that is gone.
    /// </summary>
    public ComparedRuns Reopen(string id)
    {
        var entry =
            Entries().FirstOrDefault(e => e.Record.Id == id)
            ?? throw new CommandException($"no kept comparison '{id}' (see cs2 compare list)");
        if (entry.GoneRun is { } gone)
            throw new CommandException($"{id}: run {gone} is gone (cs2 compare remove {id})");
        var opened =
            Open(entry.Record)
            ?? throw new CommandException($"{id}: run {entry.Record.B} can't be read (cs2 compare remove {id})");
        return opened with { Reopened = true };
    }

    /// <summary>
    /// What clearing the saved runs would do now, leaving out the runs <paramref name="inProgress"/> and the
    /// comparison being made of them.
    /// </summary>
    public ClearPlan Plan(IEnumerable<string> inProgress) =>
        ClearPlan.Of(_store.List(), Records, inProgress.Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal));

    /// <summary>Deletes the runs <paramref name="plan"/> names, then stops keeping the comparisons it names.</summary>
    public void Clear(ClearPlan plan)
    {
        var deleted = _store.Clear(plan);
        Remove(plan.ComparisonsDelete);
        Log.Info($"cleared {deleted} saved runs and {plan.ComparisonsDelete.Count} kept comparisons");
    }

    /// <summary>The newest kept comparison with its runs, or null when there is none or side B can't be read.</summary>
    public ComparedRuns? Newest() => Records.Count > 0 ? Open(Records[0]) : null;

    /// <summary>
    /// A kept comparison with its two runs read back; null when side B's run can't be read. A baseline that can't
    /// be read any more is shown as gone.
    /// </summary>
    public ComparedRuns? Open(Comparison record)
    {
        Run? Read(string id)
        {
            try
            {
                return _store.Read(id);
            }
            catch (RunFileException e)
            {
                Log.Warn($"{record.Id}: {e.Message}");
                return null;
            }
        }
        if (Read(record.B) is not { } b)
            return null;
        if (Read(record.A) is not { } a)
        {
            var gone = ComparedRuns.Placeholder(b, b.Setup);
            ComparedRuns.EndPlaceholder(gone, ComparisonWarnings.BaselineGone, RunState.Cancelled);
            return new ComparedRuns(record, gone, b, [ComparisonWarnings.BaselineGone]);
        }
        return new ComparedRuns(record, a, b, ComparisonWarnings.Of(a, b, RunService.Cs2Mvid));
    }

    // Off the main thread, as run files are. The list is read when the write starts, so the last write to finish
    // always holds the latest list.
    private void Save(string what) =>
        Task.Run(() =>
        {
            try
            {
                lock (_writing)
                    ComparisonsFile.Write(_store.Folder, Records);
                Log.Info($"{what} saved with the kept comparisons");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error($"{what} not saved", e);
                RunFailures.SaveFailed(_failures, what, e);
            }
        });
}
