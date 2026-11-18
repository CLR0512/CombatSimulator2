using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class RunService
{
    // The snapshot the current run fights on, kept for its life so a failure can keep it on disk.
    private Snapshot? _snapshot;

    /// <summary>The failures seen since CS2 loaded; the host's watcher and the mod record into it too.</summary>
    internal FailureLog FailureLog { get; } = new();

    // A run that ended in error (the host lost among them) or with failed targets keeps its snapshot, so a report
    // saved later can carry the save it fought on.
    private void KeepSnapshotIfFailed(Run run)
    {
        if (!ReferenceEquals(run, Current) || _snapshot is not { } snapshot)
            return;
        _snapshot = null;
        if (run.State == RunState.Error || run.Results.Values.Any(r => r.Reasons.Any(RunFailures.IsFailure)))
            FailedSnapshots.Keep(run.Id, snapshot);
    }
}
