namespace MelvorMods.CombatSimulator2.Engine.Dev;

internal enum BudgetKind
{
    /// <summary>The frame rule against a control window, with a time to usable where one applies.</summary>
    Frames,

    /// <summary>At most so many bytes.</summary>
    Bytes,

    /// <summary>At most so many of something.</summary>
    Count,

    /// <summary>At most so many milliseconds from an action to its result showing.</summary>
    Time,

    /// <summary>Smoothness limits against a control, each window passing or failing whole.</summary>
    Limits,

    /// <summary>No slower than a recorded spread; taken by a script outside the game's session.</summary>
    Reference,
}

/// <summary>
/// One cost CS2 is held to. <paramref name="UsableMs"/>: for a frames budget, the longest the window may take to be
/// usable again. <paramref name="Limit"/>: bytes or a count. <paramref name="Script"/>: what measures a budget the
/// bench can't (it needs a restart, a swapped store or a Release build), with its last
/// <paramref name="Recorded"/> value and the day it was taken.
/// </summary>
internal sealed record Budget(
    string Name,
    string Group,
    BudgetKind Kind,
    double? UsableMs = null,
    long? Limit = null,
    string? Script = null,
    string? Recorded = null,
    string? RecordedOn = null
);

/// <summary>
/// Every budget, with the same names and figures as the budgets file kept with CS2's specs: a limit changes in
/// both, in one commit.
/// </summary>
internal static class Budgets
{
    public const string Panel = "panel";
    public const string Memory = "memory";
    public const string Closed = "closed";
    public const string Smoothness = "smoothness";
    public const string Runs = "runs";

    private const double Usable = 100;
    private const double Rebuilt = 1000;

    // How long an edit may take to show in the Effect panel: what a refused change is allowed.
    private const long Shown = 1000;
    private const long Mb = 1024 * 1024;

    // The day the script-measured values below were taken.
    private const string Day = "2026-11-09";

    // The day the run and per-copy rows were set from the profile and accepted: best take of each batch, Release.
    private const string SearchDay = "2026-11-13";
    private const string RunsDay = "2026-11-10";

    public static IReadOnlyList<Budget> All { get; } =
    [
        new(
            "closed-frames",
            Closed,
            BudgetKind.Reference,
            Script: "startup.sh",
            Recorded: "p50 6.94 ms, CS2 disabled 6.94 ms",
            RecordedOn: Day
        ),
        new("closed-alloc", Closed, BudgetKind.Bytes, Limit: 0),
        new(
            "game-start",
            Closed,
            BudgetKind.Reference,
            Script: "startup.sh",
            Recorded: "start to in game 12.3–12.6 s, CS2 disabled 13.7–14.2 s",
            RecordedOn: Day
        ),
        new(
            "character-load",
            Closed,
            BudgetKind.Reference,
            Script: "startup.sh",
            Recorded: "4.2–4.4 s, CS2 disabled 4.2–4.3 s",
            RecordedOn: Day
        ),
        new("open", Panel, BudgetKind.Frames, Usable),
        new("reopen-after-history", Panel, BudgetKind.Frames, Usable),
        new("first-open", Panel, BudgetKind.Frames, Usable),
        new("close", Panel, BudgetKind.Frames, Usable),
        new("tab-simulate", Panel, BudgetKind.Frames, Usable),
        new("tab-setup", Panel, BudgetKind.Frames, Usable),
        new("tab-settings", Panel, BudgetKind.Frames, Usable),
        new("scroll-targets", Panel, BudgetKind.Frames),
        new("scroll-history", Panel, BudgetKind.Frames),
        new("scroll-modifiers", Panel, BudgetKind.Frames),
        new("scroll-results", Panel, BudgetKind.Frames),
        new("run-updating", Panel, BudgetKind.Frames),
        new("rebuild-skin", Panel, BudgetKind.Frames, Rebuilt),
        new("rebuild-language", Panel, BudgetKind.Frames, Rebuilt),
        new("scale-change", Panel, BudgetKind.Frames, Usable),
        new("open-alloc", Panel, BudgetKind.Bytes, Limit: 0),
        new("open-picker", Panel, BudgetKind.Frames, Usable),
        new("open-stats", Panel, BudgetKind.Frames, Usable),
        new("page-switch", Panel, BudgetKind.Frames, Usable),
        new("scroll-shop", Panel, BudgetKind.Frames),
        new("strip-toggle", Panel, BudgetKind.Frames),
        new("effect-update", Panel, BudgetKind.Time, Limit: Shown),
        new("smoothness", Smoothness, BudgetKind.Limits),
        new("host-peak", Memory, BudgetKind.Bytes, Limit: 1774 * Mb),
        new("host-peak-low", Memory, BudgetKind.Bytes, Limit: 1024 * Mb),
        new("host-stock", Memory, BudgetKind.Bytes, Limit: 936 * Mb),
        new("host-idle", Memory, BudgetKind.Bytes, Limit: 400 * Mb),
        new("host-stopped", Memory, BudgetKind.Count, Limit: 0),
        new(
            "copy-bare",
            Memory,
            BudgetKind.Reference,
            Script: "floors.sh",
            Recorded: "62 MB a bare copy (limit 68 MB)",
            RecordedOn: RunsDay
        ),
        new(
            "copy-fighting",
            Memory,
            BudgetKind.Reference,
            Script: "floors.sh",
            Recorded: "196 MB a fighting copy (limit 215 MB)",
            RecordedOn: RunsDay
        ),
        new(
            "game-added",
            Memory,
            BudgetKind.Reference,
            Script: "gamemem.sh",
            Recorded: "15 MB of managed heap (limit 25 MB)",
            RecordedOn: RunsDay
        ),
        new(
            "store-load",
            Closed,
            BudgetKind.Reference,
            Script: "store.sh",
            Recorded: "start to in game 14.5–14.9 s at 100 + 50 pinned, 13.8–14.2 s at 20",
            RecordedOn: Day
        ),
        new(
            "store-first-open",
            Closed,
            BudgetKind.Reference,
            Script: "store.sh",
            Recorded: "986–1066 ms, worst frame 56–100 ms",
            RecordedOn: Day
        ),
        new(
            "run-first-warm",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "0.04 s (limit 0.10 s)",
            RecordedOn: RunsDay
        ),
        new(
            "run-single",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "0.32 s (limit 0.36 s)",
            RecordedOn: RunsDay
        ),
        new(
            "run-first-cold",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "11.2 s (limit 9.5 s, goal 6.0 s)",
            RecordedOn: RunsDay
        ),
        new(
            "run-all-cold",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "18.9 s (limit 17.5 s, goal 14 s)",
            RecordedOn: RunsDay
        ),
        new(
            "run-stock-building",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "14.7 s (limit 15.5 s, goal 11 s)",
            RecordedOn: RunsDay
        ),
        new(
            "run-overhead",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "21% (limit 23%)",
            RecordedOn: RunsDay
        ),
        new(
            "run-per-target",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "137 ms (limit 145 ms)",
            RecordedOn: RunsDay
        ),
        new(
            "run-tail",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "0.98 s (limit 1.1 s)",
            RecordedOn: RunsDay
        ),
        new(
            "run-all-warm",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "6.87 s (recorded beside run-per-target)",
            RecordedOn: RunsDay
        ),
        // The gear search on Dev's bank, in the host, Release (probe 7): best of three takes.
        new(
            "search-quick",
            Runs,
            BudgetKind.Reference,
            Script: "probe 7 (024)",
            Recorded: "25.3 s (limit 45 s)",
            RecordedOn: SearchDay
        ),
        new(
            "search-slow",
            Runs,
            BudgetKind.Reference,
            Script: "probe 7 (024)",
            Recorded: "92.5 s (limit 150 s)",
            RecordedOn: SearchDay
        ),
        new(
            "search-frames",
            Runs,
            BudgetKind.Reference,
            Script: "probe 7 (024)",
            Recorded: "worst frame 25.5 ms against idle's 16.1 ms (limit idle's worst + 10 ms)",
            RecordedOn: SearchDay
        ),
        new(
            "fight-per-trial",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh",
            Recorded: "0.26 s per 1000 Golbin trials (limit 0.29 s)",
            RecordedOn: RunsDay
        ),
        new(
            "run-4-cores",
            Runs,
            BudgetKind.Reference,
            Script: "runs.sh cores=4",
            Recorded: "Debug, 2 copies: warm 11.8 s, cold 22.6 s; every run finished",
            RecordedOn: RunsDay
        ),
    ];

    public static IReadOnlyList<string> Groups { get; } = [Closed, Panel, Smoothness, Memory, Runs];

    /// <summary>
    /// The budgets <paramref name="only"/> names, by budget or by group, in the table's order; all of them for
    /// none. A name that is neither comes back in <paramref name="unknown"/>.
    /// </summary>
    public static IReadOnlyList<Budget> Select(IReadOnlyCollection<string> only, out IReadOnlyList<string> unknown)
    {
        ArgumentNullException.ThrowIfNull(only);
        unknown = [.. only.Where(o => !Groups.Contains(o) && All.All(b => b.Name != o))];
        return only.Count == 0 ? All : [.. All.Where(b => only.Contains(b.Name) || only.Contains(b.Group))];
    }
}
