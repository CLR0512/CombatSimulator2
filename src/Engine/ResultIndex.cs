using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// What a result must share with a new run's target to answer it: the game and CS2 builds, the setup's content, the
/// loadout, the requirements flag, the target, the trials, the trial limits, the early-stop precision and the
/// setup's changes (short form, "" for none). The seed is matched apart, and only when the player set one.
/// </summary>
internal sealed record ResultIdentity(
    string GameBuild,
    string Cs2Mvid,
    string SetupHash,
    string Loadout,
    bool IgnoreRequirements,
    string Target,
    int Trials,
    double EnemySeconds,
    double DungeonSeconds,
    double? StopPrecision,
    string Changes
)
{
    /// <summary>
    /// The identity of <paramref name="target"/> in <paramref name="run"/>; null without a setup hash.
    /// </summary>
    public static ResultIdentity? Of(Run run, string target)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.SetupHash is not { Length: > 0 } setup || run.Cs2Mvid is not { Length: > 0 } cs2)
            return null;
        return new ResultIdentity(
            run.GameBuild,
            cs2,
            setup,
            run.Setup.Loadout,
            run.Setup.IgnoreRequirements,
            target,
            run.Trials,
            run.Limits.EnemySeconds,
            run.Limits.DungeonSeconds,
            run.StopPrecision,
            run.Setup.Changes?.ShortForm ?? ""
        );
    }
}

/// <summary>
/// Where the finished results of saved and session runs are, by identity, newest first, so a run can reuse them
/// instead of simulating again. It keeps what identifies a result, not the result: that is read from its run's file
/// when a run reuses it, so a full store costs the game megabytes, not hundreds. A run just finished is held whole
/// until its file is written. Built off the main thread from the run store and kept up to date as runs are saved;
/// looked up on the main thread when a run starts. Godot-free.
/// </summary>
internal sealed class ResultIndex(Func<string, Run?>? read = null)
{
    private sealed record Entry(string RunId, DateTime StartedAt, string Key, ReuseOrigin Origin);

    private readonly Lock _lock = new();
    private readonly Dictionary<ResultIdentity, List<Entry>> _entries = [];

    // Runs whose file isn't written yet (or never will be): the only place their results are.
    private readonly Dictionary<string, Run> _held = new(StringComparer.Ordinal);

    // How long each target took to simulate in each run, whatever its setup: only a hint for the order targets
    // start in.
    private readonly Dictionary<string, (DateTime StartedAt, Dictionary<string, double> Seconds)> _durations = new(
        StringComparer.Ordinal
    );

    /// <summary>Results indexed.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _entries.Values.Sum(e => e.Count);
        }
    }

    /// <summary>
    /// Indexes a final run's finished results (a run indexed again replaces its entries). A run not
    /// <paramref name="saved"/> yet is held until <see cref="Saved"/> says its file is there.
    /// </summary>
    public void Add(Run run, bool saved = false)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!run.IsFinal)
            return;
        lock (_lock)
        {
            RemoveLocked(run.Id);
            if (!saved)
                _held[run.Id] = run;
            var seconds = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var result in run.Results.Values)
            {
                if (
                    result.ReusedFrom == null
                    && result.RealSeconds > 0
                    && result.State is TargetState.Finished or TargetState.Unfinished
                )
                    seconds[result.Key] = result.RealSeconds;
            }
            if (seconds.Count > 0)
                _durations[run.Id] = (run.StartedAt, seconds);
            foreach (var key in run.Targets)
            {
                if (
                    run.Results.GetValueOrDefault(key) is not { State: TargetState.Finished } result
                    || ResultIdentity.Of(run, key) is not { } identity
                )
                    continue;
                // A result that was itself reused keeps the run it was simulated in.
                var origin =
                    result.ReusedFrom
                    ?? new ReuseOrigin
                    {
                        RunId = run.Id,
                        Seed = result.RunSeed ?? run.Seed,
                        Date = run.StartedAt,
                    };
                if (!_entries.TryGetValue(identity, out var list))
                    _entries[identity] = list = [];
                list.Add(new Entry(run.Id, run.StartedAt, key, origin));
                list.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
            }
        }
    }

    /// <summary>The run's file is written: its results are read from there from now on.</summary>
    public void Saved(string runId)
    {
        lock (_lock)
            _held.Remove(runId);
    }

    /// <summary>Forgets every result.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _held.Clear();
            _durations.Clear();
        }
    }

    /// <summary>Keeps only the entries of these runs (the store's, after it dropped old ones or was cleared).</summary>
    public void Keep(IEnumerable<string> runIds)
    {
        var keep = runIds.ToHashSet(StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var id in _entries.Values.SelectMany(l => l.Select(e => e.RunId)).Distinct().ToList())
            {
                if (!keep.Contains(id))
                    RemoveLocked(id);
            }
        }
    }

    /// <summary>
    /// The newest result for <paramref name="identity"/>, simulated with <paramref name="seed"/> when one is given
    /// (any seed otherwise), with the run it was simulated in. <paramref name="opened"/> keeps the runs read for one
    /// lookup of many targets, so each file is read once; a run that can't be read any more answers nothing.
    /// </summary>
    public (TargetResult Result, ReuseOrigin Origin)? Find(
        ResultIdentity identity,
        ulong? seed,
        Dictionary<string, Run?>? opened = null
    )
    {
        List<Entry> candidates;
        lock (_lock)
        {
            if (!_entries.TryGetValue(identity, out var list))
                return null;
            candidates = [.. list.Where(e => seed == null || e.Origin.Seed == seed)];
        }
        foreach (var entry in candidates)
        {
            if (
                Open(entry.RunId, opened)?.Results.GetValueOrDefault(entry.Key)
                    is { State: TargetState.Finished } result
                && (Counted(result) || !CountsFights())
            )
                return (result, entry.Origin);
        }
        return null;
    }

    // A master holds what its monsters' fights reported, not batches of its own.
    private static bool Counted(TargetResult result) =>
        result.Parts is { } parts ? parts.TrueForAll(p => p.HasFightCounts || !p.HasEstimates) : result.HasFightCounts;

    /// <summary>
    /// Whether runs now record what the game reported in their fights: a result saved without it then can't answer a
    /// new run, which would show "not recorded" where a fresh one has numbers.
    /// </summary>
    public Func<bool> CountsFights { get; init; } = () => false;

    private Run? Open(string runId, Dictionary<string, Run?>? opened)
    {
        lock (_lock)
        {
            if (_held.TryGetValue(runId, out var held))
                return held;
        }
        if (opened != null && opened.TryGetValue(runId, out var known))
            return known;
        var run = read?.Invoke(runId);
        opened?[runId] = run;
        return run;
    }

    /// <summary>
    /// The targets in the order to start them so the copies finish together: the slowest first, by the newest run
    /// that simulated each (any setup, as only the order depends on it). Targets never simulated go first, as any of
    /// them may be slow, in the order given; ties keep the order given too.
    /// </summary>
    public IReadOnlyList<string> LongestFirst(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Dictionary<string, double> known;
        lock (_lock)
        {
            known = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (_, seconds) in _durations.Values.OrderByDescending(d => d.StartedAt))
            {
                foreach (var key in keys)
                {
                    if (!known.ContainsKey(key) && seconds.TryGetValue(key, out var value))
                        known[key] = value;
                }
            }
        }
        return
        [
            .. keys.Select((key, at) => (key, at))
                .OrderBy(k => known.ContainsKey(k.key) ? 1 : 0)
                .ThenByDescending(k => known.GetValueOrDefault(k.key))
                .ThenBy(k => k.at)
                .Select(k => k.key),
        ];
    }

    private void RemoveLocked(string runId)
    {
        _held.Remove(runId);
        _durations.Remove(runId);
        foreach (var (identity, list) in _entries.ToList())
        {
            list.RemoveAll(e => e.RunId == runId);
            if (list.Count == 0)
                _entries.Remove(identity);
        }
    }
}
