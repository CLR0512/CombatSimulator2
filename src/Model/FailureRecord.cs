namespace MelvorMods.CombatSimulator2.Model;

/// <summary>The step of a run that a log line or a failure belongs to.</summary>
internal enum RunPhase
{
    /// <summary>Snapshot, reuse lookup and eligibility checks.</summary>
    Plan,

    /// <summary>Building, preparing and resetting copies.</summary>
    Build,

    Fight,

    /// <summary>Results back, finalising and saving.</summary>
    Report,
}

/// <summary>
/// What failed: the host process in one of three ways, or a run, a target, a save, a load, or a copy that couldn't
/// be built and was dropped while the run went on with the others.
/// </summary>
internal enum FailureKind
{
    HostCrash,
    HostHang,
    HostStart,
    Run,
    Target,
    Save,
    Load,
    Copy,
}

/// <summary>
/// One failure seen this session, kept for the panel and the bug report. A host's failure is recorded once the host
/// has exited, with its dump and its event-log entry when there are.
/// </summary>
internal sealed record FailureRecord
{
    /// <summary>The most host log lines a record keeps.</summary>
    public const int HostLineLimit = 50;

    public required FailureKind Kind { get; init; }

    /// <summary>When it happened, in UTC.</summary>
    public DateTime At { get; init; } = DateTime.UtcNow;

    public string? RunId { get; init; }

    /// <summary><c>A</c> or <c>B</c> for a comparison's side.</summary>
    public char? Side { get; init; }

    public RunPhase? Phase { get; init; }

    /// <summary>Why, for the player: shown in the language of the panel, written in English in the report.</summary>
    public required Note Reason { get; init; }

    /// <summary>The exception's type and message, or the text the host failed with.</summary>
    public string? Error { get; init; }

    public int? HostPid { get; init; }

    public int? ExitCode { get; init; }

    /// <summary>The targets fighting when the host was lost.</summary>
    public string[] InFlight { get; init; } = [];

    /// <summary>The end of the host's log when it was lost, up to <see cref="HostLineLimit"/> lines.</summary>
    public string[] LastHostLines { get; init; } = [];

    /// <summary>The host's crash entry from the Windows event log, once read.</summary>
    public string? EventLogEntry { get; init; }

    /// <summary>Why there is no entry: one of <see cref="EventLogNotes"/>.</summary>
    public string? EventLogNote { get; init; }

    /// <summary>The name of the dump written for this failure, in the dump folder.</summary>
    public string? Dump { get; init; }

    /// <summary>
    /// The dev lever sent while the run was active (<c>throw</c>, <c>throw &lt;target&gt;</c>, <c>throw build</c>,
    /// <c>failfast</c>, <c>hang</c>), so a rerun can send it again; only Debug builds fill it.
    /// </summary>
    public string? Lever { get; init; }

    /// <summary>Whether the panel shows it as a banner: a dropped copy doesn't stop the run, so it has none.</summary>
    public bool HasBanner => Kind != FailureKind.Copy;

    /// <summary>The kind as logs, replies and the report write it: <c>host-crash</c>, <c>run</c>, ….</summary>
    public static string Name(FailureKind kind) =>
        kind switch
        {
            FailureKind.HostCrash => "host-crash",
            FailureKind.HostHang => "host-hang",
            FailureKind.HostStart => "host-start",
            FailureKind.Run => "run",
            FailureKind.Target => "target",
            FailureKind.Save => "save",
            FailureKind.Copy => "copy",
            _ => "load",
        };

    public static string Name(RunPhase phase) =>
        phase switch
        {
            RunPhase.Plan => "plan",
            RunPhase.Build => "build",
            RunPhase.Fight => "fight",
            _ => "report",
        };
}

/// <summary>Why a host failure has no event-log entry.</summary>
internal static class EventLogNotes
{
    public const string NotFound = "not found";
    public const string AccessRefused = "access refused";
}
