namespace MelvorMods.CombatSimulator2.Engine;

// Overrides for cs2dev: copy reuse, the warm stock's release delay, and the levers sent during a run.
internal sealed partial class RunService
{
    private readonly Lock _leverLock = new();
    private (string? RunId, string Lever)? _lever;

    /// <summary>Turns reusing copies between targets on or off for the next runs.</summary>
    public static void SetReuseCopies(bool reuse) => ReuseCopies = reuse;

    /// <summary>Sets how long after the panel closes the host's bare copies are dropped.</summary>
    public static void SetReleaseDelay(TimeSpan delay) => ReleaseDelay = delay;

    /// <summary>A run generation of the service's own counter, for a pool run that isn't the service's.</summary>
    public int NextGeneration() => ++_generation;

    /// <summary>
    /// A fault lever was sent: the next failure record of the active run, or with none active of the next run to
    /// fail, names it, so a rerun of a report can send it again. Main thread.
    /// </summary>
    public void LeverSent(string lever)
    {
        lock (_leverLock)
            _lever = (Current is { IsFinal: false } run ? run.Id : null, lever);
    }

    /// <summary>The lever sent during <paramref name="runId"/> or before it, once; any thread.</summary>
    public string? TakeLever(string? runId)
    {
        lock (_leverLock)
        {
            if (runId == null || _lever is not { } sent || (sent.RunId != null && sent.RunId != runId))
                return null;
            _lever = null;
            return sent.Lever;
        }
    }
}
