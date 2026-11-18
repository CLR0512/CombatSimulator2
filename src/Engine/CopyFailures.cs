using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// A fight slot whose copy fails to build or prepare: the slot ends and the target it would have fought fails with
/// the error, so one bad copy costs one target. The run fails only once no slot is left to fight the rest.
/// </summary>
internal sealed class CopyFailures(
    int slots,
    IPoolReport report,
    Func<TargetSpec?> takeNext,
    Func<bool> workLeft,
    Action<Exception> failRun
)
{
    private int _slotsLeft = slots;

    /// <summary>The result of a target an error stopped: its note names the error, the log holds its stack.</summary>
    public static TargetResult Result(TargetSpec target, Exception error)
    {
        var result = new TargetResult
        {
            Key = target.Key,
            Kind = target.Kind,
            State = TargetState.Unsupported,
        };
        result.Reasons.Add(Note.Of("failed: {0}: {1}", error.GetType().Name, error.Message));
        return result;
    }

    public void Failed(Exception error)
    {
        using (LogScope.Push(phase: RunPhase.Build))
            Log.Error("copy failed, dropped", error);
        report.CopyDropped($"{error.GetType().Name}: {error.Message}");
        if (takeNext() is { } target)
        {
            using (LogScope.Push(target: target.Key))
                Log.Warn("ended by its copy's failure");
            report.Done(Result(target, error));
        }
        if (Interlocked.Decrement(ref _slotsLeft) == 0 && workLeft())
            failRun(error);
    }
}
