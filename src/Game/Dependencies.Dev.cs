namespace MelvorMods.CombatSimulator2.Game;

internal static partial class Dependencies
{
    /// <summary>
    /// Names one entry that resolves as missing from the next load on: <c>patch &lt;name&gt;</c> or
    /// <c>dependency &lt;name&gt;</c>. Read at load, by the live game and the host alike, so a patch's
    /// <c>Prepare</c> sees it before Harmony runs.
    /// </summary>
    public const string DevMissFile = "dev-miss.txt";

    private static (bool Patch, string Name)? _devMiss;

    static partial void ReadDevMiss(string folder)
    {
        _devMiss = null;
        _last = null;
        var path = Path.Combine(folder, DevMissFile);
        if (!File.Exists(path))
            return;
        var words = File.ReadAllText(path).Trim().Split(' ', 2);
        if (words is [var kind, var name])
            _devMiss = (kind == "patch", name);
    }

    /// <summary>The entry the lever would name, or null when no entry has that name.</summary>
    public static Dependency? DevFind(bool patch, string name) =>
        (patch ? Patches : List).FirstOrDefault(d => d.Name == name);

    static partial void DevMissing(Dependency dep, ref bool resolves)
    {
        if (_devMiss is { } miss && miss.Name == dep.Name && miss.Patch == (dep.Kind == "patch"))
            resolves = false;
    }
}
