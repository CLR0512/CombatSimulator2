using System.Security.Cryptography;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>A failure as an unpacked report records it, for the run it belongs to.</summary>
internal sealed record ReportedFailure(string RunId, char? Side, FailureFacts Facts, string? Lever);

/// <summary>
/// An unpacked bug report, as <c>./mm report</c> leaves it: its about, runs, failures, searches and snapshots. A
/// report of format 1 opens, but has nothing to rerun.
/// </summary>
internal sealed class ReportFolder
{
    private readonly Dictionary<string, string> _snapshotNotes;

    private ReportFolder(string folder, JsonElement about, Dictionary<string, string> snapshotNotes)
    {
        Folder = folder;
        _snapshotNotes = snapshotNotes;
        Format =
            about.TryGetProperty("reportFormat", out var f)
            && f.ValueKind == JsonValueKind.Number
            && f.TryGetInt32(out var n)
                ? n
                : 1;
        GameBuild = Text(about, "gameBuild") ?? "";
        Cs2Mvid = Text(about, "cs2Mvid");
        Cs2Version = Text(about, "cs2Version");
        SaveSetupHash = Text(about, "saveSetupHash");
    }

    public string Folder { get; }
    public int Format { get; }
    public string GameBuild { get; }
    public string? Cs2Mvid { get; }
    public string? Cs2Version { get; }

    /// <summary>The setup fingerprint of the save the report carries; null without one.</summary>
    public string? SaveSetupHash { get; }

    public List<RunRecord> Runs { get; } = [];
    public List<SearchRecord> Searches { get; } = [];

    /// <summary>Newest first, as the report keeps them.</summary>
    public List<ReportedFailure> Failures { get; } = [];

    /// <summary>Opens <paramref name="folder"/>; throws <see cref="CommandException"/> when it isn't a report.</summary>
    public static ReportFolder Open(string folder)
    {
        var aboutPath = Path.Combine(folder, "about.json");
        if (!File.Exists(aboutPath) || !File.Exists(Path.Combine(folder, "summary.txt")))
            throw new CommandException($"not a CS2 report: {folder}");
        try
        {
            using var about = JsonDocument.Parse(File.ReadAllText(aboutPath));
            var report = new ReportFolder(folder, about.RootElement.Clone(), SnapshotNotes(folder));
            report.ReadRuns();
            report.ReadFailures();
            report.ReadSearches();
            return report;
        }
        catch (JsonException e)
        {
            throw new CommandException($"not a CS2 report: {folder} ({e.Message})");
        }
    }

    // A target that failed, from a run's own entry: "key: reason", the reason as the run file keeps it.
    private readonly Dictionary<string, List<(string Key, string Reason)>> _failedTargets = new(StringComparer.Ordinal);

    private void ReadRuns()
    {
        if (ReadJson("runs.json") is not { ValueKind: JsonValueKind.Array } runs)
            return;
        foreach (var entry in runs.EnumerateArray())
        {
            if (RunRecord.Read(entry) is not { } record)
                continue;
            Runs.Add(record);
            if (entry.TryGetProperty("failedTargets", out var failed) && failed.ValueKind == JsonValueKind.Array)
                _failedTargets[record.Id] =
                [
                    .. failed
                        .EnumerateArray()
                        .Select(f => f.GetString() ?? "")
                        .Select(f => f.Split(": ", 2))
                        .Select(p => (p[0], p.Length > 1 ? p[1] : "")),
                ];
        }
    }

    private void ReadSearches()
    {
        if (ReadJson("searches.json") is { } searches)
            Searches.AddRange(SearchRecord.Read(searches));
    }

    private void ReadFailures()
    {
        if (ReadJson("failures.json") is not { ValueKind: JsonValueKind.Array } failures)
            return;
        foreach (var f in failures.EnumerateArray())
        {
            if (Text(f, "run") is not { } run || Text(f, "kind") is not { } kind)
                continue;
            var side = Text(f, "side") is [var s] ? s : (char?)null;
            // A target failure names its first failed target before the error.
            var target =
                kind == FailureRecord.Name(FailureKind.Target) && Text(f, "error") is { } error
                    ? error.Split(": ", 2)[0]
                    : null;
            int? exit =
                f.TryGetProperty("exitCode", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.TryGetInt32(out var c)
                    ? c
                    : null;
            Failures.Add(
                new ReportedFailure(run, side, new FailureFacts(kind, Text(f, "phase"), target, exit), Text(f, "lever"))
            );
        }
    }

    private JsonElement? ReadJson(string name)
    {
        var path = Path.Combine(Folder, name);
        if (!File.Exists(path))
            return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // snapshots.txt: "<id>  <character>  sha256 <hash>  <n> KB  <time> UTC  <where>", the where after the last "UTC  ".
    private static Dictionary<string, string> SnapshotNotes(string folder)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(folder, "snapshots.txt");
        if (!File.Exists(path))
            return notes;
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split("  ", 2);
            var where = line.LastIndexOf(" UTC  ", StringComparison.Ordinal);
            if (parts.Length == 2 && where > 0)
                notes[parts[0]] = line[(where + " UTC  ".Length)..];
        }
        return notes;
    }

    /// <summary>
    /// The run to rerun: <paramref name="id"/>, else the newest run with a failure, else the run on screen (the
    /// first). Null when the report names a search instead.
    /// </summary>
    public RunRecord? PickRun(string? id)
    {
        if (Format < BugReport.Format)
            throw new CommandException("the report is format 1: it has no run requests (save a new report)");
        if (id != null)
            return Runs.FirstOrDefault(r => r.Id == id) ?? throw new CommandException($"no run {id} in the report");
        foreach (var failure in Failures)
        {
            if (Runs.FirstOrDefault(r => r.Id == failure.RunId) is { } failed)
                return failed;
        }
        if (Searches.FirstOrDefault(s => s.State == "failed") != null)
            return null;
        return Runs.FirstOrDefault() ?? throw new CommandException("the report holds no run");
    }

    public SearchRecord PickSearch(string? id) =>
        id == null
            ? Searches.FirstOrDefault(s => s.State == "failed")
                ?? Searches.FirstOrDefault()
                ?? throw new CommandException("the report holds no search")
            : Searches.FirstOrDefault(s => s.Id == id) ?? throw new CommandException($"no search {id} in the report");

    /// <summary>The other side of a comparison's side, when the report holds it.</summary>
    public RunRecord? Pair(RunRecord run) =>
        run.PairedWith is { } other ? Runs.FirstOrDefault(r => r.Id == other) : null;

    /// <summary>
    /// The failures the report records for <paramref name="runId"/>, oldest first. The session's failure records are
    /// gone after a restart, but a run's entry still names its failed targets: then each is one, and a dev lever's
    /// throw, which names itself in the error, is taken as the lever.
    /// </summary>
    public List<ReportedFailure> FailuresOf(string runId)
    {
        var of = Failures.Where(f => f.RunId == runId).ToList();
        of.Reverse();
        if (of.Count > 0 || !_failedTargets.TryGetValue(runId, out var failed))
            return of;
        var side =
            Runs.FirstOrDefault(r => r.Id == runId)?.Kind is { } kind
            && kind.StartsWith("comparison", StringComparison.Ordinal)
                ? kind[^1]
                : (char?)null;
        return
        [
            .. failed.Select(f => new ReportedFailure(
                runId,
                side,
                new FailureFacts(FailureRecord.Name(FailureKind.Target), FailureRecord.Name(RunPhase.Fight), f.Key),
                f.Reason.Contains("cs2dev throw", StringComparison.Ordinal) ? "throw" : null
            )),
        ];
    }

    /// <summary>
    /// The save a run is rerun on: its own snapshot when it failed and one is kept, else the report's save when the
    /// setup it holds is the run's (or <paramref name="anyway"/>). Throws with the refusal otherwise.
    /// </summary>
    public (byte[] Bytes, string Note) SnapshotFor(
        string id,
        string? snapshotEntry,
        string expectedSha,
        string? setupHash,
        bool failed,
        bool anyway
    )
    {
        if (snapshotEntry != null && File.Exists(Path.Combine(Folder, snapshotEntry)))
        {
            var bytes = File.ReadAllBytes(Path.Combine(Folder, snapshotEntry));
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedSha)
                throw new CommandException($"the snapshot of {id} doesn't match its sha256");
            return (bytes, "its own snapshot");
        }
        if (failed)
        {
            var why = _snapshotNotes.GetValueOrDefault(id) ?? "none was kept";
            throw new CommandException($"run {id} has no snapshot in the report ({why})");
        }
        var save = Directory.Exists(Path.Combine(Folder, "save"))
            ? Directory.GetFiles(Path.Combine(Folder, "save"), "*.save").FirstOrDefault()
            : null;
        if (save == null)
            throw new CommandException($"run {id} has no snapshot in the report (the save was left out)");
        if (SaveSetupHash != setupHash && !anyway)
            throw new CommandException(
                $"the save in the report isn't the setup run {id} ran (fingerprint differs): add anyway to rerun on it"
            );
        return (
            File.ReadAllBytes(save),
            SaveSetupHash == setupHash ? "the report's save" : "the report's save, anyway"
        );
    }
}
