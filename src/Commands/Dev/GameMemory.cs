using System.Runtime;
using System.Runtime.Loader;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// What the game's own process holds: private bytes, the managed heap, and how many copies of CS2 the dev host has
/// loaded (each hot reload leaves the old one in place). With <c>collect</c>, after one full compacting collection:
/// what is left is live.
/// </summary>
internal static class GameMemory
{
    public static void Run(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) == "collect")
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        var heap = ProcessMeter.Heap();
        var copies = AssemblyLoadContext.All.Count(c =>
            c.Name?.Contains("CombatSimulator2", StringComparison.Ordinal) == true
        );
        static long Mb(long bytes) => bytes / 1048576;
        call.Ok(
            $"game {Mb(ProcessMeter.PrivateBytes())} MB private, heap {Mb(heap.InUse)} MB in use, {Mb(heap.Committed)} MB committed, "
                + $"collections {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, CS2 copies loaded {copies}"
        );
    }
}
