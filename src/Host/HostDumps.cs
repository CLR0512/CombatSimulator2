using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// The host's dumps in <c>&lt;DataRoot&gt;/dumps/</c>: the runtime writes one when the host dies, and the game writes one
/// of a host that stopped answering. The newest few are kept, within a total size.
/// </summary>
internal static partial class HostDumps
{
    public const int KeepCount = 3;

    public static string Folder(string dataRoot) => Path.Combine(dataRoot, "dumps");

    /// <summary>
    /// The runtime's dump settings for a host: a Triage dump, which has every thread's managed stack in about 2 MB.
    /// The name is quoted because the runtime passes it to createdump unquoted, and the game's folder has spaces.
    /// </summary>
    public static void AddEnvironment(IDictionary<string, string> environment, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var folder = Folder(dataRoot);
        Directory.CreateDirectory(folder);
        environment["DOTNET_DbgEnableMiniDump"] = "1";
        environment["DOTNET_DbgMiniDumpType"] = "3";
        environment["DOTNET_DbgMiniDumpName"] = $"\"{Path.Combine(folder, HostDumpInfo.NamePattern)}\"";
    }

    /// <summary>The kept dumps under <paramref name="dataRoot"/>, newest first, after pruning; no folder without a root.</summary>
    public static (string? Folder, IReadOnlyList<HostDumpInfo> Dumps) Kept(string? dataRoot, long keepBytes)
    {
        if (dataRoot == null)
            return (null, []);
        var folder = Folder(dataRoot);
        Prune(folder, keepBytes);
        return (folder, List(folder));
    }

    /// <summary>The kept dumps, newest first.</summary>
    public static List<HostDumpInfo> List(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        var dumps = new List<HostDumpInfo>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("CombatSimulator2.host.*.dmp"))
        {
            if (HostDumpInfo.Parse(file.Name) is { } parsed)
                dumps.Add(new HostDumpInfo(file.Name, parsed.Pid, file.LastWriteTimeUtc, file.Length, parsed.Kind));
        }
        return [.. dumps.OrderByDescending(d => d.At)];
    }

    /// <summary>
    /// The size of the crash dump the runtime is writing, or has written, for <paramref name="pid"/> since
    /// <paramref name="since"/>; null when there is none.
    /// </summary>
    public static long? CrashDumpBytes(string folder, int pid, DateTime since)
    {
        if (!Directory.Exists(folder))
            return null;
        long? bytes = null;
        var prefix = string.Create(CultureInfo.InvariantCulture, $"CombatSimulator2.host.{pid}.");
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles(prefix + "*.dmp"))
        {
            if (file.LastWriteTimeUtc >= since && HostDumpInfo.Parse(file.Name) is { Kind: DumpKind.Crash })
                bytes = (bytes ?? 0) + file.Length;
        }
        return bytes;
    }

    /// <summary>Removes the oldest dumps past <see cref="KeepCount"/> or <paramref name="keepBytes"/>; returns their names.</summary>
    public static List<string> Prune(string folder, long keepBytes, int keepCount = KeepCount)
    {
        var removed = new List<string>();
        long total = 0;
        var kept = 0;
        foreach (var dump in List(folder))
        {
            if (kept < keepCount && total + dump.Bytes <= keepBytes)
            {
                kept++;
                total += dump.Bytes;
                continue;
            }
            try
            {
                File.Delete(Path.Combine(folder, dump.File));
                removed.Add(dump.File);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A dump still being written is pruned on a later start.
            }
        }
        return removed;
    }

    /// <summary>The longest a hung host's dump may take before the game gives up on it and kills the host.</summary>
    public static readonly TimeSpan HangDumpLimit = TimeSpan.FromSeconds(60);

    // What the runtime's createdump writes for a Triage dump: every thread's managed stack, few megabytes.
    private const uint TriageFlags = 0x1205AC;

    /// <summary>
    /// Writes a dump of a hung host before it is killed, as the runtime would have if it had crashed. Gives up after
    /// <see cref="HangDumpLimit"/>, removing the partial file. Returns the file's name, or null with the reason logged.
    /// </summary>
    public static string? WriteHang(string folder, int pid, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);
        if (!OperatingSystem.IsWindows())
            return null;
        Directory.CreateDirectory(folder);
        var name = HostDumpInfo.HangName(pid, DateTime.UtcNow);
        var path = Path.Combine(folder, name);
        string? failure = null;
        var writer = new Thread(() =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                using var file = File.Create(path);
                if (
                    !Native.MiniDumpWriteDump(
                        process.SafeHandle.DangerousGetHandle(),
                        (uint)pid,
                        file.SafeFileHandle.DangerousGetHandle(),
                        TriageFlags,
                        0,
                        0,
                        0
                    )
                )
                    failure = $"error {Marshal.GetLastPInvokeError()}";
            }
            catch (Exception e)
                when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                failure = e.Message;
            }
        })
        {
            IsBackground = true,
            Name = "cs2-hang-dump",
        };
        writer.Start();
        if (!writer.Join(HangDumpLimit))
            failure = $"hang dump abandoned after {HangDumpLimit.TotalSeconds:0} s";
        if (failure == null)
            return name;
        warn($"host {pid}: no hang dump ({failure})");
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still held by the abandoned writer: the next pruning removes it.
        }
        return null;
    }

    private static partial class Native
    {
        [LibraryImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool MiniDumpWriteDump(
            nint process,
            uint pid,
            nint file,
            uint type,
            nint exception,
            nint user,
            nint callback
        );
    }
}
