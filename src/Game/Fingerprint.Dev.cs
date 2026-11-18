using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

internal static partial class Fingerprints
{
    private static readonly Dictionary<string, Fingerprint> _taken = new(StringComparer.Ordinal);
    private static int _next;

    public static Fingerprint Take(MelvorGame game, string? label)
    {
        MainThread.Assert("Fingerprints.Take");
        var id = label ?? $"fp-{++_next}";
        var fingerprint = new Fingerprint(id, DateTime.UtcNow, Godot.Engine.GetPhysicsFrames(), Parts(game));
        _taken[id] = fingerprint;
        return fingerprint;
    }

    public static Fingerprint? Get(string id) => _taken.GetValueOrDefault(id);

    /// <summary>The parts whose hashes differ, including parts only one side has.</summary>
    public static IReadOnlyList<string> Diff(Fingerprint a, Fingerprint b) => PartDiff.Between(a.Parts, b.Parts);

    /// <summary>
    /// The save bytes of one component, named as in a part without its <c>component</c> prefix
    /// (<c>Combat.Player</c>); null when no entity has it. Main thread.
    /// </summary>
    public static byte[]? ComponentBytes(MelvorGame game, string name)
    {
        MainThread.Assert("Fingerprints.ComponentBytes");
        foreach (var entity in game.Entities.GetAllObjects())
        {
            foreach (var (component, value) in entity.Components)
            {
                if (ComponentPart(entity, component) == "component " + name)
                    return Bytes(game, w => w.Write(value));
            }
        }
        return null;
    }

    /// <summary>
    /// The runs of bytes that differ between two writes of one component, each as its offset, its length and both
    /// values in hex; the lengths too when they differ. Offsets follow the game's writer for that component.
    /// </summary>
    public static List<string> ByteRanges(byte[] a, byte[] b)
    {
        var ranges = new List<string>();
        if (a.Length != b.Length)
            ranges.Add($"length {a.Length} → {b.Length}");
        var shared = Math.Min(a.Length, b.Length);
        for (var i = 0; i < shared; i++)
        {
            if (a[i] == b[i])
                continue;
            var start = i;
            while (i < shared && a[i] != b[i])
                i++;
            ranges.Add(
                $"offset {start} length {i - start}: {Convert.ToHexString(a, start, i - start)} → {Convert.ToHexString(b, start, i - start)}"
            );
        }
        return ranges;
    }
}
