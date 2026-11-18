using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// Records each run's and each gear search's performance once a second from the mod's frame callback, panel open or
/// not: the simulating process's CPU and memory (the host's from its heartbeat, or the game's own), free memory and
/// the game's fps. Keeps the run and the search under way and the last of each. Main thread.
/// </summary>
internal static class PerfRecorder
{
    private static ProcessMeter? _game;
    private static long _next;
    private static long _searchStarted;

    /// <summary>The run under way, if any.</summary>
    public static PerfHistory? Current { get; private set; }

    /// <summary>The last run that ended.</summary>
    public static PerfHistory? Last { get; private set; }

    /// <summary>The gear search under way, if any.</summary>
    public static PerfHistory? SearchCurrent { get; private set; }

    /// <summary>The last gear search that ended.</summary>
    public static PerfHistory? SearchLast { get; private set; }

    public static void OnFrame()
    {
        var service = RunService.Instance;
        var run = service.Current is { IsFinal: false } r ? r : null;
        if (Current != null && Current.RunId != run?.Id)
        {
            Last = Current;
            Current = null;
        }
        if (run != null && Current == null)
        {
            Current = new PerfHistory(run.Id, run.SimulatedIn ?? RunService.InGame);
            _next = 0;
        }
        var search = service.Search is { IsActive: true } s ? s : null;
        if (SearchCurrent != null && SearchCurrent.RunId != search?.Id)
        {
            SearchLast = SearchCurrent;
            SearchCurrent = null;
        }
        if (search != null && SearchCurrent == null)
        {
            SearchCurrent = new PerfHistory(search.Id, search.Where);
            _searchStarted = Environment.TickCount64;
            _next = 0;
        }
        if (Current == null && SearchCurrent == null)
            return;
        var now = Environment.TickCount64;
        if (now < _next)
            return;
        _next = now + 1000;
        if (Current != null && Read(Current.Where, service.RealSeconds) is { } runSample)
            Current.Add(runSample);
        if (SearchCurrent != null && Read(SearchCurrent.Where, (now - _searchStarted) / 1000.0) is { } searchSample)
            SearchCurrent.Add(searchSample);
    }

    // One sample of the process doing the work; none while the host isn't working for it yet.
    private static PerfSample? Read(string where, double seconds)
    {
        var fps = Godot.Engine.GetFramesPerSecond();
        if (where == RunService.InHost)
        {
            // Only while the host is working: before that, any heartbeat is from the host idle or starting.
            if (RunService.HostStatus?.Invoke() is not { State: HostState.Busy, Reading: { } beat })
                return null;
            return new PerfSample(seconds, beat.CpuPercent, beat.PrivateBytes, beat.AvailableBytes, fps);
        }
        var (cpu, ram, _) = (_game ??= new ProcessMeter()).Read();
        return new PerfSample(seconds, cpu, ram, MemoryReserve.Available(), fps);
    }

    public static void Clear()
    {
        Current = Last = SearchCurrent = SearchLast = null;
        _game = null;
    }
}
