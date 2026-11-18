using System.Diagnostics;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>
/// The stages of a simulator host's start, each logged once with the milliseconds since its process started, so a
/// slow first result shows where the time went. Off until <see cref="Begin"/> (the host calls it; the live game
/// never does) and off again after the first result: later copies and runs log nothing here.
/// </summary>
internal static class StartStages
{
    private static readonly HashSet<string> _marked = new(StringComparer.Ordinal);
    private static long _processStart; // as a Stopwatch timestamp
    private static volatile bool _running;
    private static int _copies;

    /// <summary>Starts logging stages in this process.</summary>
    public static void Begin()
    {
        using var process = Process.GetCurrentProcess();
        var age = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        _processStart = Stopwatch.GetTimestamp() - (long)(age.TotalSeconds * Stopwatch.Frequency);
        _running = true;
    }

    /// <summary>Logs <paramref name="stage"/> the first time it is reached.</summary>
    public static void Mark(string stage)
    {
        if (!_running)
            return;
        lock (_marked)
        {
            if (!_marked.Add(stage))
                return;
        }
        Log.Info($"start: {stage} {Ms():0}");
    }

    /// <summary>The first target fought has its result: the last stage.</summary>
    public static void FirstResult()
    {
        Mark("first result");
        _running = false;
    }

    /// <summary>A copy's build begins; null once the first result is out.</summary>
    public static CopyLoad? CopyBuilding() => _running ? new CopyLoad(Interlocked.Increment(ref _copies)) : null;

    /// <summary>The run's setup is applied to copy <paramref name="number"/> (0: a copy built after the start).</summary>
    public static void CopyPrepared(int number)
    {
        if (_running && number > 0)
            Log.Info($"start: copy {number} prepared {Ms():0}");
    }

    private static double Ms() => Stopwatch.GetElapsedTime(_processStart).TotalMilliseconds;

    /// <summary>
    /// One copy's data load, split by the game's own loading-progress events: the second event is the package
    /// about to be read, and one follows every object registered from it.
    /// </summary>
    internal sealed class CopyLoad(int number)
    {
        private readonly long _begin = Stopwatch.GetTimestamp();
        private int _events;
        private long _reading;
        private long _firstObject;
        private long _lastObject;

        /// <summary>The copy's number among those built during the start, from 1.</summary>
        public int Number => number;

        /// <summary>The game reported loading progress.</summary>
        public void Progressed()
        {
            var now = Stopwatch.GetTimestamp();
            switch (++_events)
            {
                case 2:
                    _reading = now;
                    break;
                case 3:
                    _firstObject = now;
                    break;
            }
            _lastObject = now;
        }

        /// <summary>The data load is done: logs it with its parts, or alone if the game's events didn't come.</summary>
        public void Done()
        {
            if (!_running)
                return;
            var end = Stopwatch.GetTimestamp();
            var parts =
                _events >= 3
                    ? $"{Between(_begin, _reading):0} registering, {Between(_reading, _firstObject):0} package, {Between(_firstObject, _lastObject):0} objects, {Between(_lastObject, end):0} callbacks"
                    : $"{Between(_begin, end):0} in all";
            Log.Info($"start: copy {number} data {Ms():0} ({parts})");
        }

        private static double Between(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
    }
}
