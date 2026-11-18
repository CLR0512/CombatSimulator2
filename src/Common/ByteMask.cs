using System.Buffers.Binary;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>Takes one known value out of written bytes, so what is hashed doesn't change when only it does.</summary>
internal static class ByteMask
{
    /// <summary>
    /// Zeroes the eight bytes of <paramref name="value"/> (little-endian, as the game's writer puts them) when they
    /// occur exactly once in <paramref name="bytes"/>, and says whether it did. Found nowhere or more than once,
    /// nothing is changed: the hash then moves with the value, which only costs a result that could have been
    /// reused.
    /// </summary>
    public static bool ZeroOnce(Span<byte> bytes, ulong value)
    {
        Span<byte> pattern = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(pattern, value);
        var at = bytes.IndexOf(pattern);
        if (at < 0 || bytes[(at + 1)..].IndexOf(pattern) >= 0)
            return false;
        bytes.Slice(at, pattern.Length).Clear();
        return true;
    }
}
