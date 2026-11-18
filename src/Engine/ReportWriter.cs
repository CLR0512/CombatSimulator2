using System.Globalization;
using System.IO.Compression;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>A log a report carries: the file, its name in the zip, and how much of its end goes in.</summary>
internal sealed record ReportLog(string Path, string Entry, long Bound);

/// <summary>A failed run's snapshot kept on disk, as a report lists it.</summary>
internal sealed record ReportSnapshot(
    string Id,
    string Character,
    string Sha256,
    long Bytes,
    DateTime TakenAt,
    string Path
);

/// <summary>Everything a report holds, gathered before it is written.</summary>
/// <param name="Settings">CS2's settings file as saved; null when there is none.</param>
/// <param name="DumpFolder">Where <paramref name="Dumps"/> are; null when there is no dump folder.</param>
/// <param name="Save">The character's save as the game writes it, and its file name; null when left out.</param>
/// <param name="Snapshots">The kept snapshots of failed runs, newest first; listed always, put in only with the save.</param>
/// <param name="Panel">What the panel showed, names masked, or <c>panel closed</c>; null leaves panel.txt out.</param>
internal sealed record ReportContents(
    ReportSummary Summary,
    string About,
    string? Settings,
    string Runs,
    string Failures,
    IReadOnlyList<ReportLog> Logs,
    string EventLog,
    string? DumpFolder,
    IReadOnlyList<HostDumpInfo> Dumps,
    (string Name, byte[] Bytes)? Save = null,
    string? Searches = null,
    IReadOnlyList<ReportSnapshot>? Snapshots = null,
    string? Panel = null
);

/// <summary>
/// Writes a report zip: every text redacted, each log's end within its bound, then the kept snapshots newest first and
/// the newest dump, each only while the zip stays under the size limit (a snapshot goes before the dump). The newest
/// <see cref="Keep"/> reports are kept. The character's save and the snapshots go in only when the player left the
/// save on; saved runs never do.
/// </summary>
internal static class ReportWriter
{
    public const long SizeLimit = 20L * 1024 * 1024;
    public const int Keep = 10;
    private const string Prefix = "CombatSimulator2-report-";

    public static string NameFor(DateTime local) =>
        Prefix + local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";

    /// <summary>
    /// Writes the report and prunes the folder to the newest <see cref="Keep"/> reports within
    /// <paramref name="keepBytes"/> in all; the report just written always stays.
    /// </summary>
    public static ReportResult Write(
        ReportContents contents,
        string folder,
        RedactionNames names,
        long sizeLimit = SizeLimit,
        long keepBytes = long.MaxValue
    )
    {
        ArgumentNullException.ThrowIfNull(contents);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, contents.Summary.ReportFile);
        var temp = path + ".tmp";
        var dump = contents.DumpFolder != null && contents.Dumps.Count > 0 ? contents.Dumps[0] : null;
        var snapshots = contents.Save != null ? contents.Snapshots?.Count ?? 0 : 0;
        try
        {
            var entries = Zip(contents, temp, names, dump, snapshots);
            if (dump != null && new FileInfo(temp).Length > sizeLimit)
            {
                dump = null;
                entries = Zip(contents, temp, names, dump, snapshots);
            }
            while (snapshots > 0 && new FileInfo(temp).Length > sizeLimit)
                entries = Zip(contents, temp, names, dump, --snapshots);
            File.Move(temp, path, overwrite: true);
            Prune(folder, keepBytes);
            return new ReportResult(
                contents.Summary.ReportFile,
                new FileInfo(path).Length,
                entries,
                Redacted(contents.Summary, names),
                folder
            );
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static ReportSummary Redacted(ReportSummary summary, RedactionNames names) =>
        summary with
        {
            LastRunId = summary.LastRunId is { } run ? Redact.Text(run, names) : null,
            LastError = summary.LastError is { } error ? Redact.Text(error, names) : null,
        };

    private static List<string> Zip(
        ReportContents contents,
        string path,
        RedactionNames names,
        HostDumpInfo? dump,
        int snapshots
    )
    {
        File.Delete(path);
        var entries = new List<string>();
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Text(string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(Redact.Text(text, names));
            entries.Add(name);
        }
        Text("summary.txt", ReportText.Summary(contents.Summary) + "\n");
        Text("about.json", contents.About);
        if (contents.Settings != null)
            Text("settings.json", contents.Settings);
        Text("runs.json", contents.Runs);
        Text("failures.json", contents.Failures);
        if (contents.Searches != null)
            Text("searches.json", contents.Searches);
        if (contents.Panel != null)
            Text("panel.txt", contents.Panel + "\n");
        foreach (var log in contents.Logs)
        {
            if (LogTail.Text(log.Path, log.Bound) is { } tail)
                Text(log.Entry, tail);
        }
        Text("eventlog.txt", contents.EventLog);
        Text("dumps.txt", DumpList(contents.Dumps, dump));
        var kept = contents.Snapshots ?? [];
        Text("snapshots.txt", SnapshotList(kept, snapshots, contents.Save != null));
        if (contents.Save is { } save)
        {
            // The save is the game's own binary format: it names the character, never the Windows account.
            var entry = zip.CreateEntry("save/" + save.Name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
                stream.Write(save.Bytes);
            entries.Add("save/" + save.Name);
        }
        foreach (var snapshot in kept.Take(snapshots))
        {
            var name = SnapshotEntry(snapshot.Id);
            try
            {
                zip.CreateEntryFromFile(snapshot.Path, name, CompressionLevel.Optimal);
                entries.Add(name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Pruned since it was listed: snapshots.txt still names it.
            }
        }
        if (dump != null && contents.DumpFolder != null)
        {
            zip.CreateEntryFromFile(
                Path.Combine(contents.DumpFolder, dump.File),
                "dumps/" + dump.File,
                CompressionLevel.Optimal
            );
            entries.Add("dumps/" + dump.File);
        }
        return entries;
    }

    /// <summary>A kept snapshot's entry in the zip.</summary>
    public static string SnapshotEntry(string id) => $"snapshots/{id}.save";

    private static string SnapshotList(IReadOnlyList<ReportSnapshot> kept, int included, bool saveOn)
    {
        if (kept.Count == 0)
            return "No snapshots of failed runs kept.\n";
        var text = new StringBuilder();
        for (var i = 0; i < kept.Count; i++)
        {
            var snapshot = kept[i];
            var where =
                !saveOn ? "left out by choice"
                : i < included ? "in this zip"
                : "not in this zip: over the size limit";
            text.Append(
                CultureInfo.InvariantCulture,
                $"{snapshot.Id}  {snapshot.Character}  sha256 {snapshot.Sha256}  {snapshot.Bytes / 1024} KB  {snapshot.TakenAt:yyyy-MM-dd HH:mm:ss} UTC  {where}\n"
            );
        }
        return text.ToString();
    }

    private static string DumpList(IReadOnlyList<HostDumpInfo> dumps, HostDumpInfo? included)
    {
        if (dumps.Count == 0)
            return "No host dumps.\n";
        var text = new StringBuilder();
        foreach (var dump in dumps)
        {
            var where = dump == included ? "in this zip" : "not in this zip";
            text.Append(
                CultureInfo.InvariantCulture,
                $"{dump.File}  {dump.Kind.ToString().ToLowerInvariant()}  {dump.Bytes / 1024} KB  {dump.At:yyyy-MM-dd HH:mm:ss} UTC  {where}\n"
            );
        }
        if (included != null)
            text.Append("A dump isn't redacted: it holds the host's memory as it was.\n");
        return text.ToString();
    }

    private static void Prune(string folder, long keepBytes)
    {
        var reports = Directory
            .GetFiles(folder, Prefix + "*.zip")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        long total = 0;
        var kept = 0;
        foreach (var report in reports)
        {
            var bytes = new FileInfo(report).Length;
            if (kept == 0 || (kept < Keep && total + bytes <= keepBytes))
            {
                kept++;
                total += bytes;
                continue;
            }
            var old = report;
            try
            {
                File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Open elsewhere: it goes with a later report.
            }
        }
    }
}
