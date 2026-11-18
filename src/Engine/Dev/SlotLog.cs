using MelvorMods.CombatSimulator2.Common;

namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>
/// One line per fight slot of the newest pool run, for <c>cs2dev timings</c>. A run in the simulator host
/// writes them to the host's log, where the game reads them back; a run in the game keeps them here.
/// </summary>
internal static class SlotLog
{
    private const string Marker = "slots of the run:";
    private const string HostLog = "CombatSimulator2.host.log";
    private static IReadOnlyList<string> _inGame = [];
    private static DateTime _inGameAt;

    public static void Keep(IReadOnlyList<string> lines)
    {
        _inGame = lines;
        _inGameAt = DateTime.UtcNow;
        Log.Info(Marker);
        foreach (var line in lines)
            Log.Info(line);
    }

    /// <summary>
    /// The newest run's slot lines: this process's own, or the host log's under <paramref name="dataRoot"/>
    /// when that was written later.
    /// </summary>
    public static IReadOnlyList<string> Newest(string? dataRoot)
    {
        if (dataRoot == null)
            return _inGame;
        var path = Path.Combine(dataRoot, "logs", HostLog);
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < _inGameAt)
            return _inGame;
        var lines = LogTail.Lines(path, 400);
        var marker = Array.FindLastIndex(lines, l => l.Contains(Marker, StringComparison.Ordinal));
        if (marker < 0)
            return _inGame;
        return
        [
            .. lines
                .Skip(marker + 1)
                .TakeWhile(l => l.Contains("slot ", StringComparison.Ordinal))
                .Select(l => l[l.IndexOf("slot ", StringComparison.Ordinal)..].TrimEnd()),
        ];
    }
}
