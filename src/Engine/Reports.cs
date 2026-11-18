using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>Where bug reports go, how much space they and the host's dumps may take, and saving one.</summary>
internal static class Reports
{
    /// <summary>CS2's own folder, set by the mod: reports, logs and settings are under it.</summary>
    public static string? DataRoot { get; set; }

    /// <summary>CS2's version, as its manifest gives it.</summary>
    public static string Cs2Version =>
        ModTable.Process.FindMod("CombatSimulator2") is { } record
        && ModTable.Get<string>(record, ModTable.Version) is { } version
            ? version
            : typeof(Reports).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>The most space crash dumps may take, and again bug reports; set from the player's settings.</summary>
    public static long CrashFilesBytes { get; set; } = Preferences.DefaultCrashFilesMegabytes * 1024L * 1024;

    public static string? Folder => DataRoot is { } root ? Path.Combine(root, "reports") : null;

    private static int _reporting;

    /// <summary>
    /// Saves a bug report on a thread of its own and calls back on the main thread with the result, or with why it
    /// couldn't be saved. A run in progress goes on untouched: the report reads it once, here, and reads files with
    /// shared access. <paramref name="includeSave"/> writes the live character's save into it, as runs do.
    /// </summary>
    public static void Save(RunService service, bool includeSave, Action<ReportResult?, string?> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        MainThread.Assert("Reports.Save");
        if (DataRoot is not { } root)
        {
            done(null, "CS2 isn't loaded");
            return;
        }
        if (Interlocked.Exchange(ref _reporting, 1) == 1)
        {
            done(null, "a report is being saved already");
            return;
        }
        var host = RunService.HostStatus?.Invoke();
        var state = BugReport.Snapshot(service, host, includeSave);
        new Thread(() =>
        {
            ReportResult? result = null;
            string? error = null;
            try
            {
                var contents = BugReport.Gather(state, root, host, DateTime.Now);
                result = ReportWriter.Write(
                    contents,
                    Folder!,
                    RedactionNames.Current(),
                    sizeLimit: Math.Min(ReportWriter.SizeLimit, CrashFilesBytes),
                    keepBytes: CrashFilesBytes
                );
                Log.Info($"report saved: {result.File} ({result.Bytes / 1024} KB)");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error =
                    e is UnauthorizedAccessException ? $"the reports folder isn't writable ({e.Message})" : e.Message;
                Log.Warn("report not saved", e);
            }
            finally
            {
                Volatile.Write(ref _reporting, 0);
            }
            MainThread.Post(() => done(result, error));
        })
        {
            IsBackground = true,
            Name = "cs2-report",
        }.Start();
    }
}
