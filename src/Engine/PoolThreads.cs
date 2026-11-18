using MelvorMods.CombatSimulator2.Common;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>The threads a pool runs on: background, below normal priority, named for the logs and the debugger.</summary>
internal static class PoolThreads
{
    /// <summary>
    /// Starts <paramref name="body"/> in its starter's run and side; its phase, target and copy are its own.
    /// </summary>
    public static Thread Start(string name, Action body)
    {
        var scope = LogScope.Current with { Phase = null, Target = null, Copy = null };
        var thread = new Thread(() =>
        {
            using var _ = LogScope.Enter(scope);
            body();
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = name,
        };
        thread.Start();
        return thread;
    }
}
