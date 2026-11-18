using System.Runtime.InteropServices;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>The machine a report comes from, as far as a crash or a slow run may depend on it.</summary>
internal sealed record SystemFacts(string Os, int Cores, long TotalMemoryBytes, long FreeMemoryBytes)
{
    public static SystemFacts Read()
    {
        var (total, free) = MemoryReserve.Physical();
        return new SystemFacts(RuntimeInformation.OSDescription, Environment.ProcessorCount, total, free);
    }
}
