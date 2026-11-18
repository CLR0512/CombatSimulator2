namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// A kept comparison with the headers of its two runs, for a list; a header is null when that run is no longer on
/// disk.
/// </summary>
/// <param name="Character">The character it was simulated for; null when no run of it says.</param>
public sealed record KeptComparison(Comparison Record, RunHeader? A, RunHeader? B, string? Character = null)
{
    /// <summary>
    /// Whether a side was simulated on another game build than <paramref name="gameBuild"/>, the one running now.
    /// </summary>
    public bool AnotherBuild(string gameBuild) =>
        new[] { A, B }.Any(h => h != null && !string.Equals(h.GameBuild, gameBuild, StringComparison.Ordinal));

    /// <summary>Whether a side was made on a game build CS2 wasn't checked with.</summary>
    public bool Unchecked => new[] { A, B }.Any(h => h?.Checked == false);

    /// <summary>The id of a run the comparison names that is gone (side A's first); null when both are there.</summary>
    public string? GoneRun =>
        A == null ? Record.A
        : B == null ? Record.B
        : null;
}

/// <summary>
/// The comparisons CS2 keeps: the last <see cref="Max"/> that finished, newest first. A comparison only links two
/// saved runs, so keeping one costs a small record and those two runs staying past the keep limit. Not thread-safe.
/// </summary>
public sealed class KeptComparisons
{
    public const int Max = 5;

    private readonly List<Comparison> _entries = [];

    public KeptComparisons(IEnumerable<Comparison>? entries = null)
    {
        // Oldest first, so the newest ends up first and a repeated pair keeps its newest record.
        foreach (var entry in (entries ?? []).Reverse())
            Add(entry);
    }

    /// <summary>The kept records, newest first.</summary>
    public IReadOnlyList<Comparison> Entries => _entries;

    /// <summary>
    /// Keeps <paramref name="record"/> as the newest. An entry with the same two runs, in either order (the same
    /// comparison swapped, or run again from earlier results), is replaced by it. Returns the record that had to go
    /// to stay within <see cref="Max"/>, or null.
    /// </summary>
    public Comparison? Add(Comparison record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _entries.RemoveAll(e => SameRuns(e, record));
        _entries.Insert(0, record);
        if (_entries.Count <= Max)
            return null;
        var dropped = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        return dropped;
    }

    /// <summary>Stops keeping the comparison with that id; false when there is none. Its runs stay saved.</summary>
    public bool Remove(string id) => _entries.RemoveAll(e => e.Id == id) > 0;

    /// <summary>The runs of every entry: what the run store keeps past its limit.</summary>
    public IReadOnlySet<string> RunIds =>
        _entries.SelectMany(e => new[] { e.A, e.B }).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a kept comparison names the run, so deleting it would break that comparison.</summary>
    public bool Uses(string runId) => _entries.Any(e => e.A == runId || e.B == runId);

    private static bool SameRuns(Comparison x, Comparison y) =>
        (x.A == y.A && x.B == y.B) || (x.A == y.B && x.B == y.A);
}
