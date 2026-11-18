using System.Text.Json;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>A failed run's or search's snapshot as kept on disk.</summary>
internal sealed record KeptSnapshot(
    string Id,
    string Character,
    string Sha256,
    string GameBuild,
    DateTime TakenAt,
    long Bytes,
    string Path
);

/// <summary>
/// The snapshots of runs and searches that failed, in <c>&lt;DataRoot&gt;/snapshots/</c>, so a bug report saved
/// later (after a restart too) can carry the very save the run fought on. Each is the save's bytes,
/// <c>&lt;id&gt;.save</c>, and what it was taken from, <c>&lt;id&gt;.json</c>; the newest few are kept, within the
/// space crash files may take together with the host's dumps.
/// </summary>
internal static class FailedSnapshots
{
    private const string SaveExtension = ".save";
    private const string FactsExtension = ".json";

    private static readonly Lock Writing = new();

    public static string Folder(string dataRoot) => System.IO.Path.Combine(dataRoot, "snapshots");

    private sealed record Facts(string Character, string Sha256, string GameBuild, DateTime TakenAt);

    /// <summary>Writes the snapshot of <paramref name="id"/> on a thread of its own, then prunes.</summary>
    public static void Keep(string id, Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Reports.DataRoot is not { } root)
            return;
        var keepBytes = Reports.CrashFilesBytes;
        new Thread(() => Write(root, id, snapshot, keepBytes)) { IsBackground = true, Name = "cs2-snapshot" }.Start();
    }

    private static void Write(string root, string id, Snapshot snapshot, long keepBytes)
    {
        var folder = Folder(root);
        try
        {
            lock (Writing)
            {
                Directory.CreateDirectory(folder);
                var save = System.IO.Path.Combine(folder, id + SaveExtension);
                File.WriteAllBytes(save + ".tmp", snapshot.Bytes);
                File.Move(save + ".tmp", save, overwrite: true);
                File.WriteAllText(
                    System.IO.Path.Combine(folder, id + FactsExtension),
                    JsonSerializer.Serialize(
                        new Facts(snapshot.Character, snapshot.Sha256, snapshot.GameBuild, snapshot.TakenAt),
                        RunFile.Options
                    )
                );
                Prune(root, keepBytes);
            }
            Log.Info($"snapshot of {id} kept ({snapshot.Bytes.Length / 1024} KB)");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"snapshot of {id} not kept", e);
        }
    }

    /// <summary>The kept snapshots, newest first.</summary>
    public static IReadOnlyList<KeptSnapshot> List(string dataRoot)
    {
        var folder = Folder(dataRoot);
        if (!Directory.Exists(folder))
            return [];
        var kept = new List<KeptSnapshot>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*" + SaveExtension))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(file.Name);
            var facts = ReadFacts(System.IO.Path.Combine(folder, id + FactsExtension));
            kept.Add(
                new KeptSnapshot(
                    id,
                    facts?.Character ?? "",
                    facts?.Sha256 ?? "",
                    facts?.GameBuild ?? "",
                    facts?.TakenAt ?? file.LastWriteTimeUtc,
                    file.Length,
                    file.FullName
                )
            );
        }
        return [.. kept.OrderByDescending(k => k.TakenAt)];
    }

    /// <summary>Removes the oldest snapshots past the count or the space left beside the host's dumps.</summary>
    public static void Prune(string dataRoot, long keepBytes)
    {
        var kept = List(dataRoot);
        var remove = KeptSnapshots.ToRemove(
            kept.Select(k => new SnapshotFile(k.Id, k.TakenAt, k.Bytes)),
            DumpBytes(dataRoot),
            keepBytes
        );
        foreach (var id in remove)
        {
            foreach (var extension in (string[])[SaveExtension, FactsExtension])
            {
                try
                {
                    File.Delete(System.IO.Path.Combine(Folder(dataRoot), id + extension));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A file still open is removed by a later prune.
                }
            }
        }
    }

    // The host's dumps share the space; they are pruned on their own.
    private static long DumpBytes(string dataRoot)
    {
        var dumps = System.IO.Path.Combine(dataRoot, "dumps");
        return Directory.Exists(dumps) ? new DirectoryInfo(dumps).EnumerateFiles("*.dmp").Sum(f => f.Length) : 0;
    }

    private static Facts? ReadFacts(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Facts>(File.ReadAllText(path), RunFile.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
