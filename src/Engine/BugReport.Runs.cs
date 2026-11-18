using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

// The runs a report holds: the run on screen and the newest saved ones, each with what it ran.
internal static partial class BugReport
{
    /// <summary>A comparison side's kind and the other side's run id.</summary>
    internal sealed record RunKind(string Kind, string? PairedWith);

    private const string RequestKey = "request";

    private static Dictionary<string, RunKind> Kinds(ComparedRuns? comparison)
    {
        var kinds = new Dictionary<string, RunKind>(StringComparer.Ordinal);
        if (comparison is { } c)
        {
            kinds[c.A.Id] = new RunKind("comparison A", c.B.Id);
            kinds[c.B.Id] = new RunKind("comparison B", c.A.Id);
        }
        return kinds;
    }

    // The run on screen first, then the newest saved runs, RunCount in all. A run whose snapshot is kept and goes in
    // the report names its entry.
    private static List<Dictionary<string, object?>> Runs(State state, HashSet<string> includeSnapshots)
    {
        var runs = new List<Dictionary<string, object?>>();
        if (state.Current is Dictionary<string, object?> current)
            runs.Add(current);
        foreach (var header in RunStore.Shared?.List() ?? [])
        {
            if (runs.Count >= RunCount)
                break;
            if (runs.Any(r => (string?)r["id"] == header.Id))
                continue;
            try
            {
                runs.Add(Summary(RunStore.Shared!.Read(header.Id), state.Kinds));
            }
            catch (RunFileException e)
            {
                runs.Add(new Dictionary<string, object?> { ["id"] = header.Id, ["unreadable"] = e.Message });
            }
        }
        foreach (var run in runs)
        {
            if (run.GetValueOrDefault(RequestKey) is RunRecord record && includeSnapshots.Contains(record.Id))
                record.Snapshot = ReportWriter.SnapshotEntry(record.Id);
        }
        return runs;
    }

    // The summary as written: its record in run files' camel case.
    private static Dictionary<string, object?> Written(Dictionary<string, object?> summary)
    {
        if (summary.GetValueOrDefault(RequestKey) is not RunRecord record)
            return summary;
        return new Dictionary<string, object?>(summary) { [RequestKey] = record.ToJson() };
    }

    // The run's name and its loadout's are never written: they are what the player typed.
    private static Dictionary<string, object?> Summary(Run run, IReadOnlyDictionary<string, RunKind> kinds)
    {
        var kind =
            kinds.GetValueOrDefault(run.Id)
            ?? new RunKind(run.Setup.Changes is { IsEmpty: false } ? "what-if" : "run", null);
        var hash =
            run.State == RunState.Finished ? ResultsHashInput.Brief(run.Targets.Select(k => run.Results[k])) : null;
        return new()
        {
            ["id"] = run.Id,
            ["kind"] = kind.Kind,
            ["state"] = run.State.ToString(),
            ["where"] = run.SimulatedIn,
            ["gameBuild"] = run.GameBuild,
            ["seed"] = run.Seed,
            ["targets"] = run.Targets.Count,
            ["done"] = run.Results.Values.Count(r => r.IsFinal),
            ["failedTargets"] = run
                .Results.Values.Where(r => r.Reasons.Any(RunFailures.IsFailure))
                .Select(r => $"{r.Key}: {string.Join("; ", r.Reasons.Select(n => n.English()))}")
                .ToList(),
            ["timings"] = run.Timings,
            ["error"] = run.Error?.English(),
            ["startedAtUtc"] = run.StartedAt,
            ["endedAtUtc"] = run.EndedAt,
            [RequestKey] = RunRecord.From(run, kind.Kind, kind.PairedWith, PairSeeds.Of(run), hash, null),
        };
    }

    private static HashSet<string> KeptIds(IEnumerable<ReportSnapshot> kept) =>
        kept.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);

    // Pruned first, as the host's dumps are, so the report lists only what stays.
    private static List<ReportSnapshot> KeptSnapshots(string dataRoot)
    {
        FailedSnapshots.Prune(dataRoot, Reports.CrashFilesBytes);
        return
        [
            .. FailedSnapshots
                .List(dataRoot)
                .Select(k => new ReportSnapshot(k.Id, k.Character, k.Sha256, k.Bytes, k.TakenAt, k.Path)),
        ];
    }
}
