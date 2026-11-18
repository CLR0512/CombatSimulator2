using System.Text;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>The seed a target is fought with, from the run's seed and the target's key.</summary>
internal static class TargetSeeds
{
    /// <summary>
    /// The target's seed: FNV-1a over the run seed and the key, then one SplitMix64 step, so it is independent of
    /// order and copy count.
    /// </summary>
    public static ulong Of(ulong runSeed, string targetKey)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        Span<byte> seedBytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(seedBytes, runSeed);
        foreach (var b in seedBytes)
            hash = (hash ^ b) * prime;
        foreach (var b in Encoding.UTF8.GetBytes(targetKey))
            hash = (hash ^ b) * prime;
        var z = hash + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
