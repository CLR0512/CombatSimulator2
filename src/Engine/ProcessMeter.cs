using System.Diagnostics;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>This process's CPU use since the last reading, and its memory, for the live performance line.</summary>
internal sealed class ProcessMeter
{
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private TimeSpan _lastCpu;
    private TimeSpan _lastWall;

    public ProcessMeter()
    {
        using var self = Process.GetCurrentProcess();
        _lastCpu = self.TotalProcessorTime;
    }

    /// <summary>CPU use since the last reading, as a percent of the whole machine, and private bytes now.</summary>
    public (double CpuPercent, long PrivateBytes, long PeakPrivateBytes) Read()
    {
        using var self = Process.GetCurrentProcess();
        var cpu = self.TotalProcessorTime;
        var wall = _wall.Elapsed;
        var percent = Percent(
            (cpu - _lastCpu).TotalSeconds,
            (wall - _lastWall).TotalSeconds,
            Environment.ProcessorCount
        );
        _lastCpu = cpu;
        _lastWall = wall;
        return (percent, self.PrivateMemorySize64, self.PeakPagedMemorySize64);
    }

    /// <summary>Private bytes now, for readings taken more often than the CPU's.</summary>
    public static long PrivateBytes()
    {
        using var self = Process.GetCurrentProcess();
        return self.PrivateMemorySize64;
    }

    /// <summary>
    /// The managed heap: bytes in use, and bytes the runtime holds for it as of its last collection. Private bytes
    /// less the second is native memory, which no collection gives back.
    /// </summary>
    public static (long InUse, long Committed) Heap() =>
        (GC.GetTotalMemory(false), GC.GetGCMemoryInfo().TotalCommittedBytes);

    /// <summary>CPU seconds over wall seconds, across every core: 100 is the whole machine busy.</summary>
    public static double Percent(double cpuSeconds, double wallSeconds, int cores)
    {
        if (wallSeconds <= 0 || cores <= 0 || cpuSeconds <= 0)
            return 0;
        return Math.Clamp(cpuSeconds / wallSeconds / cores * 100, 0, 100);
    }
}
