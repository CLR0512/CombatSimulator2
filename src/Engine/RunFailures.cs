using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>The failures of runs, saves and loads, as records for the session's failure log.</summary>
internal static class RunFailures
{
    public static void SaveFailed(FailureLog log, string runId, Exception error) =>
        log.Record(
            new FailureRecord
            {
                Kind = FailureKind.Save,
                RunId = runId,
                Phase = RunPhase.Report,
                Reason = Note.Of("{0} wasn't saved: {1}", runId, error.Message),
                Error = $"{error.GetType().Name}: {error.Message}",
            }
        );

    /// <summary>Records a saved run that couldn't be opened.</summary>
    public static void LoadFailed(FailureLog log, string runId, Exception error) =>
        log.Record(
            new FailureRecord
            {
                Kind = FailureKind.Load,
                RunId = runId,
                Reason = Note.Of("{0} couldn't be opened: {1}", runId, error.Message),
                Error = $"{error.GetType().Name}: {error.Message}",
            }
        );

    // A failure of the run itself, not of the host: the host's watcher records those with the host's details.
    public static void RunFailed(FailureLog log, string runId, char? side, Note reason, Exception error)
    {
        if (error is HostException { Lost: true })
            return;
        log.Record(
            new FailureRecord
            {
                Kind = FailureKind.Run,
                RunId = runId,
                Side = side,
                Reason = reason,
                Error = $"{error.GetType().Name}: {error.Message}",
            }
        );
    }

    /// <summary>Records a copy that failed to build and was dropped while its run went on.</summary>
    public static void CopyDropped(FailureLog log, string runId, char? side, string error) =>
        log.Record(
            new FailureRecord
            {
                Kind = FailureKind.Copy,
                RunId = runId,
                Side = side,
                Phase = RunPhase.Build,
                Reason = Note.Of("a copy failed to build and was dropped"),
                Error = error,
            }
        );

    // Once per run: how many targets failed with an error, and the first error.
    public static void TargetsFailed(FailureLog log, Run run, char? side)
    {
        var failed = run
            .Results.Values.Where(r => r.State == TargetState.Unsupported && r.Reasons.Any(IsFailure))
            .ToList();
        if (failed.Count == 0)
            return;
        log.Record(
            new FailureRecord
            {
                Kind = FailureKind.Target,
                RunId = run.Id,
                Side = side,
                Phase = RunPhase.Fight,
                Reason = Note.Plural("{0} target failed", "{0} targets failed", failed.Count, failed.Count),
                Error = $"{failed[0].Key}: {failed[0].Reasons.First(IsFailure).English()}",
            }
        );
    }

    /// <summary>The note a target ends with when an error stopped it.</summary>
    public static bool IsFailure(Note note) => note.Msg == "failed: {0}: {1}";
}
