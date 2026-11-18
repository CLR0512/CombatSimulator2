using System.ComponentModel;
using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;

namespace MelvorMods.CombatSimulator2.Host.Dev;

/// <summary>
/// With a core count standing in for the machine's (cs2dev host cores=), holds each host to that many cores from
/// its first heartbeat. Its runtime was already told the count through its environment, so its thread pool and
/// collector are sized for it from the start; only the game's process is never held.
/// </summary>
internal static class HostCores
{
    private static int _held;

    public static void Hold(int? pid)
    {
        var cores = DefaultCopies.CoresOverride;
        if (pid is not { } id || cores is <= 0 or >= 64 || !OperatingSystem.IsWindows())
            return;
        if (Interlocked.Exchange(ref _held, id) == id)
            return;
        try
        {
            using var process = Process.GetProcessById(id);
            process.ProcessorAffinity = (nint)((1L << cores) - 1);
            Log.Info($"dev: host {id} held to {cores} cores");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            Log.Warn($"dev: host {id} not held to {cores} cores", e);
        }
    }
}
