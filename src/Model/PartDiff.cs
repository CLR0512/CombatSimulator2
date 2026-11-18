namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Compares two maps of part name to hash, such as two fingerprints of a game.</summary>
internal static class PartDiff
{
    /// <summary>
    /// The parts whose hashes differ, including parts only one side has, in ordinal order, leaving out
    /// <paramref name="excluded"/>.
    /// </summary>
    public static IReadOnlyList<string> Between(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        IReadOnlySet<string>? excluded = null
    ) =>
        [
            .. before
                .Keys.Union(after.Keys, StringComparer.Ordinal)
                .Where(k => excluded == null || !excluded.Contains(k))
                .Where(k =>
                    !before.TryGetValue(k, out var x)
                    || !after.TryGetValue(k, out var y)
                    || !string.Equals(x, y, StringComparison.Ordinal)
                )
                .Order(StringComparer.Ordinal),
        ];
}
