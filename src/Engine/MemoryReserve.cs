using System.Runtime.InteropServices;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

internal enum ReserveDecision
{
    Build,

    /// <summary>Not now: try again once memory frees.</summary>
    Wait,

    /// <summary>Not even one copy fits with nothing else built: the run can't go on.</summary>
    OutOfMemory,
}

/// <summary>
/// How many copies the machine's memory allows. A copy is built only if the machine keeps at least the reserve free
/// afterwards, and a run starts with as many fight slots as fit with each copy's fighting memory counted. When memory
/// is short, building ahead stops first, then fight slots wait for memory to free, never below one copy. Results
/// never depend on the copy count, so fewer copies only take longer.
/// </summary>
internal sealed partial class MemoryReserve(long reserveBytes, Func<long> availableBytes)
{
    public const long StartEstimate = 150L * 1024 * 1024;

    /// <summary>
    /// What a run takes beyond its copies however many fight: the garbage its heaps hold between collections
    /// (measured: a host with one copy fighting peaks 390–490 MB above the copy).
    /// </summary>
    public const long RunBytes = 300L * 1024 * 1024;

    /// <summary>
    /// What each fighting copy adds to that (measured: 345 MB a copy all told at 2 copies, 200–280 MB at 4).
    /// </summary>
    public const long FightBytes = 200L * 1024 * 1024;

    /// <summary>Bytes a copy takes, from the last build (it starts at 150 MB).</summary>
    public long CopyEstimate { get; private set; } = StartEstimate;

    public long ReserveBytes => reserveBytes;

    public static MemoryReserve? For(long reserveBytes) =>
        reserveBytes > 0 ? new MemoryReserve(reserveBytes, Available) : null;

    public void Built(long bytes)
    {
        if (bytes > 0)
            CopyEstimate = bytes;
    }

    /// <param name="live">Copies alive or being built now.</param>
    /// <param name="preBuild">The build is ahead of need rather than for a fight slot.</param>
    public ReserveDecision Decide(int live, bool preBuild) =>
        Decide(availableBytes(), CopyEstimate, reserveBytes, live, preBuild);

    public static ReserveDecision Decide(long available, long copyEstimate, long reserve, int live, bool preBuild)
    {
        if (reserve <= 0 || available - copyEstimate >= reserve)
            return ReserveDecision.Build;
        if (preBuild || live > 0)
            return ReserveDecision.Wait;
        return ReserveDecision.OutOfMemory;
    }

    /// <summary>
    /// Fight slots a run starts with: the most, up to <paramref name="wanted"/>, whose copies fit while fighting,
    /// and at least one. <paramref name="stock"/> copies are built already.
    /// </summary>
    public int Fighters(int wanted, int stock) => Fighters(availableBytes(), reserveBytes, wanted, stock, CopyEstimate);

    public static int Fighters(long available, long reserve, int wanted, int stock, long copyEstimate)
    {
        for (var fighters = wanted; fighters > 1; fighters--)
        {
            if (available - Cost(fighters, Math.Max(0, fighters - stock), copyEstimate) >= reserve)
                return fighters;
        }
        return Math.Min(1, wanted);
    }

    /// <summary>True when one more copy can join a stock of <paramref name="stock"/> and all of them still fight.</summary>
    public bool StockFits(int stock) => availableBytes() - Cost(stock + 1, 1, CopyEstimate) >= reserveBytes;

    /// <summary>Bytes a process needs, beyond what it holds, to build some copies and have some fighting.</summary>
    public static long Cost(int fighters, int toBuild, long copyEstimate) =>
        RunBytes + fighters * FightBytes + toBuild * copyEstimate;

    public Note OutOfMemoryReason() => OutOfMemoryReason(CopyEstimate, reserveBytes, availableBytes());

    public static Note OutOfMemoryReason(long copyEstimate, long reserve, long available) =>
        Note.Of(
            "not enough free memory for one copy (needs about {0} MB with {1} MB free): close other programs, or simulate in the game's process",
            (copyEstimate + reserve) / (1024 * 1024),
            available / (1024 * 1024)
        );

    /// <summary>The machine's available physical memory (free plus what the OS can take back), or 0.</summary>
    public static long Available()
    {
        if (!OperatingSystem.IsWindows())
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailablePhysical : 0;
    }

    /// <summary>The machine's physical memory, total and free; zeros when Windows can't say.</summary>
    public static (long Total, long Available) Physical()
    {
        if (!OperatingSystem.IsWindows())
            return (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 0);
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? ((long)status.TotalPhysical, (long)status.AvailablePhysical) : (0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
