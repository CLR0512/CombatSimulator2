using System.Globalization;
using System.Text;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// Gathers what a bug report holds. <see cref="Snapshot"/> takes what only the main thread may read (the run on screen,
/// the comparison); <see cref="Gather"/> then reads files and saved runs on any thread.
/// </summary>
internal static partial class BugReport
{
    public const int RunCount = 5;
    private const long LogBound = 5L * 1024 * 1024;
    private const long GodotBound = 1L * 1024 * 1024;
    private const long DevBound = 2L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The main thread's part: the run on screen and which comparison sides it and others are.</summary>
    /// <param name="SaveNote">Why the save is left out; null when it is in.</param>
    internal sealed record State(
        object? Current,
        IReadOnlyDictionary<string, BugReport.RunKind> Kinds,
        IReadOnlyList<FailureRecord> Failures,
        string HostState,
        string? HostReason,
        (string Name, byte[] Bytes)? Save,
        string? SaveNote,
        string? SaveSetupHash,
        IReadOnlyList<SearchRecord> Searches,
        string Panel
    );

    public static State Snapshot(RunService service, IHostStatus? host, bool includeSave)
    {
        ArgumentNullException.ThrowIfNull(service);
        (string, byte[])? save = null;
        string? saveNote = "left out by choice";
        string? saveSetupHash = null;
        if (includeSave)
        {
            if (LiveGame.Find() is { } game)
            {
                var snapshot = LiveGame.TakeSnapshot(game);
                save = (Safe(snapshot.Character) + ".save", snapshot.Bytes);
                saveSetupHash = Fingerprints.SetupHash(game);
                saveNote = null;
            }
            else
                saveNote = "no character loaded";
        }
        var kinds = Kinds(service.Comparison);
        return new State(
            service.Current is { } run ? Summary(run, kinds) : null,
            kinds,
            service.FailureLog.Newest(),
            host == null ? "unknown" : Host.Name(host.State),
            host?.Reason?.English(),
            save,
            saveNote,
            saveSetupHash,
            SearchRecords(service),
            RunService.PanelState?.Invoke() ?? "panel closed"
        );
    }

    // A character's name as a file name: letters, digits, '-' and '_' kept.
    private static string Safe(string name)
    {
        var safe = new string([.. name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);
        return safe.Length > 0 ? safe : "character";
    }

    public static ReportContents Gather(State state, string dataRoot, IHostStatus? host, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var logs = Path.Combine(dataRoot, "logs");
        var (dumpFolder, dumps) = host?.KeptDumps() ?? (null, []);
        var snapshots = KeptSnapshots(dataRoot);
        var included = state.Save != null ? KeptIds(snapshots) : [];
        var runs = Runs(state, included);
        var lastRun = state.Current as Dictionary<string, object?> ?? runs.FirstOrDefault();
        var summary = new ReportSummary(
            Reports.Cs2Version,
            LiveGame.GameBuild,
            lastRun?["id"] as string,
            lastRun?["state"] as string,
            state.Failures is [var last, ..] ? LastError(last) : null,
            ReportWriter.NameFor(now)
        );
        return new ReportContents(
            summary,
            About(now, state.SaveNote, state.SaveSetupHash),
            ReadSettings(dataRoot),
            JsonSerializer.Serialize(runs.Select(Written), Json),
            JsonSerializer.Serialize(state.Failures.Select(Failure), Json),
            Logs(logs),
            EventLog(state.Failures),
            dumpFolder,
            dumps,
            state.Save,
            Searches(dataRoot, state.Searches, included),
            snapshots,
            state.Panel
        );
    }

    // The kind, the reason, what failed when it isn't the host (the host's details are in its logs), and the phase.
    private static string LastError(FailureRecord failure)
    {
        var kind = failure.Kind == FailureKind.Target ? "targets" : FailureRecord.Name(failure.Kind).Replace('-', ' ');
        var text = $"{kind}: {failure.Reason.English()}";
        if (failure.Error is { } error && failure.HostPid == null)
            text += $" ({error})";
        return failure.Phase is { } phase ? $"{text}, phase {FailureRecord.Name(phase)}" : text;
    }

    /// <summary>The report's format: 2 adds each run's request, kept snapshots and the panel's state.</summary>
    public const int Format = 2;

    private static string About(DateTime now, string? saveNote, string? saveSetupHash)
    {
        var facts = SystemFacts.Read();
        var about = new Dictionary<string, object?>
        {
            ["reportFormat"] = Format,
            ["cs2Version"] = Reports.Cs2Version,
            ["cs2Mvid"] = RunService.Cs2Mvid,
            ["gameBuild"] = LiveGame.GameBuild,
            ["mods"] = ModTable
                .Process.Mods()
                .Select(m => new Dictionary<string, object?>
                {
                    ["id"] = m.Key,
                    ["version"] = ModTable.Get<string>(m.Value, ModTable.Version),
                })
                .ToList(),
            ["os"] = facts.Os,
            ["cores"] = facts.Cores,
            ["totalMemoryMB"] = facts.TotalMemoryBytes / 1048576,
            ["freeMemoryMB"] = facts.FreeMemoryBytes / 1048576,
            ["savedAtUtc"] = now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["save"] = saveNote == null ? "included" : $"not included: {saveNote}",
            ["saveSetupHash"] = saveSetupHash,
        };
        return JsonSerializer.Serialize(about, Json);
    }

    private static string? ReadSettings(string dataRoot)
    {
        try
        {
            return File.ReadAllText(Path.Combine(dataRoot, Standalone.SettingsFile));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?> Failure(FailureRecord f) =>
        new()
        {
            ["kind"] = FailureRecord.Name(f.Kind),
            ["atUtc"] = f.At,
            ["run"] = f.RunId,
            ["side"] = f.Side?.ToString(),
            ["phase"] = f.Phase is { } phase ? FailureRecord.Name(phase) : null,
            ["reason"] = f.Reason.English(),
            ["error"] = f.Error,
            ["hostPid"] = f.HostPid,
            ["exitCode"] = f.ExitCode,
            ["inFlight"] = f.InFlight,
            ["lastHostLines"] = f.LastHostLines,
            ["eventLogEntry"] = f.EventLogEntry,
            ["eventLogNote"] = f.EventLogNote,
            ["dump"] = f.Dump,
            ["lever"] = f.Lever,
        };

    private static List<ReportLog> Logs(string folder)
    {
        var logs = new List<ReportLog>();
        void Add(string name, long bound, string entry) =>
            logs.Add(new ReportLog(Path.Combine(folder, name), entry, bound));
        foreach (
            var name in (string[])
                [
                    "CombatSimulator2.log",
                    "CombatSimulator2.previous.log",
                    "CombatSimulator2.host.log",
                    "CombatSimulator2.host.previous.log",
                    "CombatSimulator2.host.crash.log",
                ]
        )
            Add(name, LogBound, "logs/" + name);
        foreach (
            var name in (string[])
                [
                    "CombatSimulator2.host.godot.log",
                    "CombatSimulator2.host.godot.previous.log",
                    "CombatSimulator2.host.godot.crash.log",
                ]
        )
            Add(name, GodotBound, "logs/" + name);
        // Only a development launch writes these.
        foreach (var name in (string[])["mods.log", "mods.CombatSimulator2.host.log"])
            Add(name, DevBound, "logs/dev/" + name);
        return logs;
    }

    private static string EventLog(IReadOnlyList<FailureRecord> failures)
    {
        var crashes = failures.Where(f => f.Kind == FailureKind.HostCrash).ToList();
        if (crashes.Count == 0)
            return "No host crashed this session.\n";
        var text = new StringBuilder();
        foreach (var crash in crashes)
        {
            text.Append(
                CultureInfo.InvariantCulture,
                $"host {crash.HostPid?.ToString(CultureInfo.InvariantCulture) ?? "?"} at {crash.At:yyyy-MM-dd HH:mm:ss} UTC: "
            );
            text.Append(crash.EventLogEntry ?? $"no entry ({crash.EventLogNote ?? EventLogNotes.NotFound})")
                .Append("\n\n");
        }
        return text.ToString();
    }

    // HostHealth's names for host states, without naming the Host layer.
    private static class Host
    {
        public static string Name(HostState state) =>
            state == HostState.NotStarted ? "not-started" : state.ToString().ToLowerInvariant();
    }
}
