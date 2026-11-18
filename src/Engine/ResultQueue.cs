using System.Collections.Concurrent;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// Worker output on its way to the main thread. Each item carries the run generation it was made
/// for; items from a cancelled or replaced run are dropped when drained. Drained a bounded amount per frame.
/// </summary>
internal sealed class ResultQueue
{
    private const int MaxPerFrame = 16;

    private readonly ConcurrentQueue<(int Generation, Action Apply)> _queue = new();

    public void Post(int generation, Action apply) => _queue.Enqueue((generation, apply));

    /// <summary>Main thread: applies up to <see cref="MaxPerFrame"/> items of <paramref name="generation"/>.</summary>
    public int Drain(int generation)
    {
        var applied = 0;
        for (var i = 0; i < MaxPerFrame && _queue.TryDequeue(out var item); i++)
        {
            if (item.Generation != generation)
                continue;
            item.Apply();
            applied++;
        }
        return applied;
    }

    public void Clear() => _queue.Clear();
}
