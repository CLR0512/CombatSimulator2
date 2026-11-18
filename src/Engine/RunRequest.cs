using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// What to run: the setup, the targets, and how. <c>Where</c>: <c>host</c>, <c>game</c>, or null for the setting.
/// <c>Fresh</c> simulates every target instead of reusing earlier results; <c>StopPrecision</c> stops targets early
/// at that relative precision (null: full trials). <c>Limits</c>: per-trial limits (null: the defaults).
/// <c>TargetSeeds</c>: a run seed per target that overrides <c>Seed</c> for that target, both for simulating and
/// for finding earlier results to reuse. <c>NextRunCopies</c>: bare copies the host builds during this run for the run
/// that follows it. <c>ChangeOutcomes</c>: for a setup with changes, every change as checked, kept on the run.
/// </summary>
internal sealed record RunRequest(
    Setup Setup,
    IReadOnlyList<TargetSpec> Targets,
    int Trials,
    ulong? Seed,
    int Copies,
    string? Where = null,
    bool Fresh = false,
    double? StopPrecision = null,
    TrialLimits? Limits = null,
    IReadOnlyDictionary<string, ulong>? TargetSeeds = null,
    int NextRunCopies = 0,
    List<ChangeOutcome>? ChangeOutcomes = null,
    char? Side = null
);

/// <summary>A snapshot of what runs cost and where they go, for the panel.</summary>
/// <param name="SearchPerf">The gear search under way's performance, if any.</param>
/// <param name="LastSearchPerf">The last gear search's.</param>
/// <param name="GameProcess">The game's own process, read once a second.</param>
/// <param name="FreeBytes">The machine's free memory.</param>
internal sealed record RunStatus(
    int DefaultCopies,
    PerfHistory? CurrentPerf,
    PerfHistory? LastPerf,
    PerfHistory? SearchPerf,
    PerfHistory? LastSearchPerf,
    (double CpuPercent, long PrivateBytes, long PeakPrivateBytes) GameProcess,
    long FreeBytes,
    HostState HostState,
    Note? HostReason,
    HostReading? Host
);
