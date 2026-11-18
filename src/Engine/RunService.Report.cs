using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

// The report's surface for the panel, which reaches the engine only through the run service.
internal sealed partial class RunService
{
    /// <summary>Saves a bug report off the main thread (see <see cref="Reports.Save"/>).</summary>
    public void SaveReport(bool includeSave, Action<ReportResult?, string?> done) =>
        Reports.Save(this, includeSave, done);

    public static string Cs2Version => Reports.Cs2Version;

    /// <summary>
    /// Set by the mod: what the panel shows, as <c>cs2 ui</c> gives it with the names a player typed masked; null
    /// while it is closed. A bug report takes it on the main thread.
    /// </summary>
    public static Func<string?>? PanelState { get; set; }

    /// <summary>The last search of each kind this session, for the bug report.</summary>
    internal Dictionary<Model.Search.SearchKind, Search.SearchRun> LastSearches { get; } = [];

    public static long CrashFilesBytes
    {
        get => Reports.CrashFilesBytes;
        set => Reports.CrashFilesBytes = value;
    }
}
