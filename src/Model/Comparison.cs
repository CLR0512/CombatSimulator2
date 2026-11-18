namespace MelvorMods.CombatSimulator2.Model;

public enum ComparisonKind
{
    /// <summary>Two setups simulated on the same targets.</summary>
    TwoSetups,

    /// <summary>The setup as it is now against a saved run's results.</summary>
    NowAgainstSaved,

    /// <summary>
    /// A setup against the same setup with changes (side B's <see cref="Setup.Changes"/>); otherwise as two setups.
    /// </summary>
    WhatIf,
}

/// <summary>
/// Two runs of the same targets compared: A is the baseline (setup A, or the saved run), B the other side. Each side
/// is an ordinary saved run; this record only links them.
/// </summary>
public sealed class Comparison
{
    public const string IdPrefix = "cmp-";

    /// <summary>The comparison's id: <c>cmp-</c> followed by side B's run id.</summary>
    public string Id { get; set; } = "";
    public ComparisonKind Kind { get; set; }

    /// <summary>Side A's run id: the baseline.</summary>
    public string A { get; set; } = "";

    /// <summary>Side B's run id.</summary>
    public string B { get; set; } = "";

    /// <summary>The targets, as side A lists them.</summary>
    public List<string> Targets { get; set; } = [];

    /// <summary>Side A's run seed; side B fights each target with the seed A's result came from.</summary>
    public ulong Seed { get; set; }

    /// <summary>Whether the player set the seed.</summary>
    public bool SeedSet { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The CS2 build that made the comparison.</summary>
    public string Cs2Mvid { get; set; } = "";

    public static string IdFor(string runB) => IdPrefix + runB;

    /// <summary>
    /// What two setups compared are: a what-if when both start from the same loadout and only
    /// <paramref name="b"/> has changes, else two setups.
    /// </summary>
    public static ComparisonKind KindOf(Setup a, Setup b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Loadout == b.Loadout && a.Changes == null && b.Changes != null
            ? ComparisonKind.WhatIf
            : ComparisonKind.TwoSetups;
    }
}

/// <summary>The run seed each target of a side was simulated with, so the other side can match it.</summary>
public static class PairSeeds
{
    /// <summary>
    /// The seed of each target of <paramref name="run"/>: that of the run its result was simulated in when reused,
    /// else the seed it was fought with, else the run's seed.
    /// </summary>
    public static IReadOnlyDictionary<string, ulong> Of(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var seeds = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var key in run.Targets)
        {
            var result = run.Results.GetValueOrDefault(key);
            seeds[key] = result?.ReusedFrom?.Seed ?? result?.RunSeed ?? run.Seed;
        }
        return seeds;
    }
}

/// <summary>
/// What the player should know before trusting a comparison: the two sides differ in more than the setup.
/// </summary>
public static class ComparisonWarnings
{
    /// <summary>
    /// The warnings for <paramref name="a"/> against <paramref name="b"/>: their game builds differ (both named), or
    /// the baseline was simulated by a CS2 build other than <paramref name="cs2Mvid"/>.
    /// </summary>
    public static IReadOnlyList<Note> Of(Run a, Run b, string cs2Mvid)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var notes = new List<Note>();
        if (!string.Equals(a.GameBuild, b.GameBuild, StringComparison.Ordinal))
            notes.Add(Note.Of("The sides come from different game builds: {0} and {1}.", a.GameBuild, b.GameBuild));
        if (!string.Equals(a.Cs2Mvid, cs2Mvid, StringComparison.Ordinal))
            notes.Add(Note.Of("The baseline was simulated by an earlier version of CS2."));
        return notes;
    }

    /// <summary>The note shown when a saved comparison's baseline can't be read any more.</summary>
    public static Note BaselineGone => Note.Of("The baseline run is gone.");
}

/// <summary>
/// A comparison with its two runs, as shown. Before side B starts, <paramref name="B"/> is a placeholder with A's
/// targets queued and no id.
/// </summary>
/// <param name="Warnings">
/// What differs between the sides besides the setup (see <see cref="ComparisonWarnings"/>).
/// </param>
public sealed record ComparedRuns(Comparison Record, Run A, Run B, IReadOnlyList<Note> Warnings)
{
    public bool IsFinal => A.IsFinal && B.IsFinal;

    /// <summary>The comparison's state: B's once A has finished, else A's.</summary>
    public RunState State => A.State == RunState.Finished ? B.State : A.State;

    /// <summary>Whether side B has a run of its own (it started, or it was the saved baseline's partner).</summary>
    public bool BStarted => B.Id.Length > 0;

    /// <summary>Whether both sides are finished runs of setups, so they can be shown the other way round.</summary>
    public bool CanSwap => IsFinal && BStarted && A.Id.Length > 0 && Record.Kind != ComparisonKind.NowAgainstSaved;

    /// <summary>The comparison this one is the swap of; null when it was run as it is.</summary>
    public ComparedRuns? SwappedFrom { get; init; }

    /// <summary>
    /// Whether this is a kept comparison opened again, not one just run: it is shown as it finished, and says
    /// nothing about the sides the player is setting up now.
    /// </summary>
    public bool Reopened { get; init; }

    /// <summary>
    /// The same two runs with the sides exchanged: no fight is needed, since both sides of a target share a seed.
    /// The warnings are made again for the new baseline against <paramref name="cs2Mvid"/>.
    /// </summary>
    public ComparedRuns Swapped(string cs2Mvid)
    {
        var record = new Comparison
        {
            Id = Comparison.IdFor(A.Id),
            Kind = Comparison.KindOf(B.Setup, A.Setup),
            A = B.Id,
            B = A.Id,
            Targets = [.. Record.Targets],
            Seed = Record.Seed,
            SeedSet = Record.SeedSet,
            CreatedAt = Record.CreatedAt,
            Cs2Mvid = Record.Cs2Mvid,
        };
        return new ComparedRuns(record, B, A, ComparisonWarnings.Of(B, A, cs2Mvid)) { SwappedFrom = this };
    }

    /// <summary>
    /// A placeholder side B for <paramref name="a"/>'s targets: queued, with <paramref name="setup"/>.
    /// </summary>
    public static Run Placeholder(Run a, Setup setup)
    {
        ArgumentNullException.ThrowIfNull(a);
        var run = new Run
        {
            Setup = setup,
            GameBuild = a.GameBuild,
            Checked = a.Checked,
            Seed = a.Seed,
            Trials = a.Trials,
            Limits = a.Limits,
            StopPrecision = a.StopPrecision,
            Targets = [.. a.Targets],
            State = RunState.Starting,
            StartedAt = a.StartedAt,
        };
        foreach (var key in a.Targets)
        {
            var source = a.Results.GetValueOrDefault(key);
            run.Results[key] = new TargetResult
            {
                Key = key,
                Name = source?.Name ?? key,
                Kind = source?.Kind ?? TargetKind.Enemy,
                State = TargetState.Queued,
            };
        }
        return run;
    }

    /// <summary>
    /// Ends a placeholder side B that never started: every target cancelled with <paramref name="reason"/>.
    /// </summary>
    public static void EndPlaceholder(Run b, Note reason, RunState state)
    {
        ArgumentNullException.ThrowIfNull(b);
        foreach (var result in b.Results.Values.Where(r => !r.IsFinal))
        {
            result.State = TargetState.Cancelled;
            result.Reasons.Clear();
            result.Reasons.Add(reason);
        }
        b.State = state;
        b.EndedAt = DateTime.UtcNow;
    }
}
