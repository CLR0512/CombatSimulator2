using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class RunService
{
    /// <summary>Frame callback: applies worker output and raises <see cref="Changed"/>.</summary>
    public void OnFrame()
    {
        if (_pendingB != null && Comparison is { A.IsFinal: true })
            StartSideB();
        if (_queue.Drain(_generation) > 0)
            _changed = true;
        Search?.OnFrame();
        if (!_changed)
            return;
        _changed = false;
        Changed?.Invoke();
    }

    // Main thread: a target's final result was applied.
    private void ResultArrived(Run run)
    {
        _lastResultAt = _clock.Elapsed.TotalSeconds;
        if (run.Timings is { FirstResult: 0 } timings)
            timings.FirstResult = _lastResultAt;
    }

    // Worker output → main thread, tagged with the generation it belongs to.
    private sealed class Report(RunService service, int generation, string runId, char? side) : IPoolReport
    {
        private LogScope.Restore Scope(RunPhase? phase = null) => LogScope.Push(run: runId, side: side, phase: phase);

        public void State(string key, TargetState state) =>
            service._queue.Post(
                generation,
                () =>
                {
                    if (service.Current is not { } run || MasterParts.Find(run, key) is not { IsFinal: false } result)
                        return;
                    result.State = state;
                    if (
                        Ids.IsPart(key) && run.Results.GetValueOrDefault(Ids.MasterKey(Ids.MasterOf(key))) is { } master
                    )
                        MasterParts.Settle(master);
                }
            );

        public void MainSkill(string? skill) =>
            service._queue.Post(
                generation,
                () =>
                {
                    using var scope = Scope();
                    if (service.Current is { } run)
                    {
                        run.MainSkill = skill;
                        Log.Info($"{run.Id} main skill {skill ?? "unknown"}");
                    }
                }
            );

        public void Done(TargetResult result) =>
            service._queue.Post(
                generation,
                () =>
                {
                    if (service.Current is not { } run || MasterParts.Find(run, result.Key) is not { } shown)
                        return;
                    result.Name = shown.Name;
                    result.Unsupported.AddRange(Dependencies.OptionalMissing());
                    MasterParts.Put(run, result);
                    service.ResultArrived(run);
                }
            );

        public void CopyDropped(string error) => RunFailures.CopyDropped(service.FailureLog, runId, side, error);

        public void Progress(string key, int trialsDone) =>
            service._queue.Post(
                generation,
                () =>
                {
                    // Transient: the final result replaces it.
                    if (
                        service.Current is { } run
                        && MasterParts.Find(run, key) is { IsFinal: false, State: TargetState.Fighting } result
                    )
                        result.TrialsDone = trialsDone;
                }
            );

        public void Finished(PoolStats stats) =>
            service._queue.Post(
                generation,
                () =>
                {
                    using var scope = Scope(RunPhase.Report);
                    service.LastStats = stats;
                    if (service.Current is { IsFinal: false, Timings: { } timings })
                        timings.Take(stats);
                    Log.Info(
                        $"pool: peak {stats.PeakCopies} copies, built {stats.Built} ({stats.PreBuiltUsed} ahead), build {stats.BuildSeconds:0.0} s over {stats.WallSeconds:0.0} s"
                    );
                    if (service.Current is not { IsFinal: false } run)
                        return;
                    foreach (var result in run.Results.Values.Where(r => !r.IsFinal))
                    {
                        if (result.Parts != null)
                        {
                            MasterParts.End(
                                result,
                                TargetState.Unsupported,
                                Note.Of("the pool ended without a result for it")
                            );
                            continue;
                        }
                        result.State = TargetState.Unsupported;
                        result.Reasons.Add(Note.Of("the pool ended without a result for it"));
                    }
                    service.Finish(run, RunState.Finished);
                }
            );

        public void Failed(Exception error) =>
            service._queue.Post(
                generation,
                () =>
                {
                    using var scope = Scope();
                    Log.Error("run failed", error);
                    if (service.Current is not { IsFinal: false })
                        return;
                    var reason = error switch
                    {
                        HostException host => host.Reason,
                        NotEnoughMemoryException memory => memory.Reason,
                        _ => Note.Of("failed: {0}: {1}", error.GetType().Name, error.Message),
                    };
                    RunFailures.RunFailed(service.FailureLog, runId, side, reason, error);
                    service.Fail(reason);
                }
            );
    }
}
