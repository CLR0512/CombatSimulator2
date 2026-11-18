using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Host;

// The host side of a failure the game sees: the client hands over what it knew when the host failed.
internal sealed partial class HostClient
{
    private volatile IHostListener? _starting; // the run a host is being started for

    private void HealthFailed(FailureKind kind, Note reason, int? code)
    {
        var failed = new HostCrash.Failed(
            kind,
            reason,
            code,
            _process?.Pid,
            _health.StartedAt ?? DateTime.UtcNow,
            (_listener ?? _starting)?.Running ?? default
        );
        ThreadPool.QueueUserWorkItem(_ => HostCrash.Record(failed, DataRoot));
    }

    public (string? Folder, IReadOnlyList<HostDumpInfo> Dumps) KeptDumps() =>
        HostDumps.Kept(DataRoot, RunService.CrashFilesBytes);
}

/// <summary>
/// A failed host becomes a failure record: how it failed, its pid and exit code, the run and targets it had, the end
/// of its log, and once it has exited, its dump and its entry in the Windows event log. The client notes what it knew
/// under its health's lock, while the run is still attached, and the record is built here, off that lock.
/// </summary>
internal static class HostCrash
{
    internal sealed record Failed(
        FailureKind Kind,
        Note Reason,
        int? Code,
        int? Pid,
        DateTime Started,
        (string? RunId, char? Side, string[]? InFlight) Running
    );

    /// <summary>A hung host's stacks show where it hangs: it is dumped before it is killed.</summary>
    public static void BeforeKill(FailureKind? failedAs, int pid, string? root)
    {
        if (failedAs == FailureKind.HostHang && root != null)
            HostDumps.WriteHang(HostDumps.Folder(root), pid, Log.Warn);
    }

    /// <summary>Records <paramref name="failed"/>; blocks until the host has exited, so call it off the main thread.</summary>
    public static void Record(Failed failed, string? root)
    {
        var (kind, reason, code, pid, started, running) = failed;
        // The log first: the next host's start moves it aside.
        var lines =
            root == null
                ? []
                : LogTail.Lines(Path.Combine(root, "logs", HostLogFiles.HostLog), FailureRecord.HostLineLimit);
        string? dump = null;
        string? entry = null;
        string? note = null;
        if (pid is { } id && kind is FailureKind.HostCrash or FailureKind.HostHang)
        {
            WaitForExit(id);
            if (root != null)
                HostLogFiles.CopyAsCrash(Path.Combine(root, "logs"), Log.Warn);
            if (root != null)
                dump = HostDumps
                    .List(HostDumps.Folder(root))
                    .FirstOrDefault(d => d.Pid == id && d.At >= started.AddSeconds(-1))
                    ?.File;
            if (kind == FailureKind.HostCrash)
                (entry, note) = CrashEventLog.Read(id, started);
        }
        // What the client knew of the run, else where the host's last lines put it.
        var (phase, inFlight) = running.InFlight is { Length: > 0 } fighting
            ? ((RunPhase?)RunPhase.Fight, fighting)
            : FromLines(lines, running.RunId);
        var record = new FailureRecord
        {
            Kind = kind,
            RunId = running.RunId,
            Side = running.Side,
            Phase = phase,
            Reason = reason,
            HostPid = pid,
            ExitCode = code,
            InFlight = inFlight,
            LastHostLines = lines,
            EventLogEntry = entry,
            EventLogNote = note,
            Dump = dump,
        };
        if (entry != null)
        {
            using (LogScope.Push(run: record.RunId, side: record.Side))
                Log.Error($"host {pid} crash entry: {entry}");
        }
        RunService.Instance.FailureLog.Record(record);
    }

    private static (RunPhase? Phase, string[] InFlight) FromLines(string[] lines, string? runId)
    {
        if (runId == null)
            return (null, []);
        var (phase, inFlight) = HostLineScope.Of(lines, runId);
        foreach (var known in Enum.GetValues<RunPhase>())
        {
            if (FailureRecord.Name(known) == phase)
                return (known, inFlight);
        }
        return (null, inFlight);
    }

    // A crashed host is gone at once; a dying or hung one goes once the game has killed it, after its dump.
    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            process.WaitForExit(HostDumps.HangDumpLimit + TimeSpan.FromSeconds(15));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Already gone.
        }
    }
}
