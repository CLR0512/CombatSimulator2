using System.Text;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// A game id as a short number for the setup line: FNV-1a over the id's UTF-8, folded to two or three bytes. Zero
/// is never produced: it stands for "none". The reader hashes every id of a registry in its own build and looks the
/// number up, so no table of ids is shared and a build that adds ids breaks nothing.
/// </summary>
public static class IdHash
{
    /// <summary>The hash of <paramref name="id"/> in <paramref name="bytes"/> bytes (2 or 3).</summary>
    public static uint Of(string id, int bytes)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (bytes is not (2 or 3))
            throw new ArgumentOutOfRangeException(nameof(bytes), "an id hash is 2 or 3 bytes");
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(id))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        var bits = bytes * 8;
        var folded = (hash >> bits ^ hash) & (1u << bits) - 1;
        return folded == 0 ? 1 : folded;
    }
}
