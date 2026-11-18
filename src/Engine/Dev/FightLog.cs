using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>
/// One log line per target fought, for <c>cs2dev batches</c>: the run and side, the slot, where its copy came from,
/// how many targets the copy fought before and the last of them, the seed, and a short hash of each batch. Two runs
/// that should be equal are compared line by line: the first batch hash that differs says where they part, and the
/// rest of the line what that copy had been through. A target is fought whole in one copy, so every batch of a line
/// shares it. A run in the simulator host writes to the host's log, where the game reads the lines back.
/// </summary>
internal static class FightLog
{
    private const string Marker = "fought run=";
    private const string HostLog = "CombatSimulator2.host.log";

    private sealed class Seen
    {
        public string Origin = "built";
        public int Fights;
        public string Last = "-";
    }

    private static readonly ConditionalWeakTable<Copy, Seen> _copies = [];

    // Where the dump starts in the host's log: its length when recording was switched on.
    private static long? _from;

    /// <summary>Notes where a fight slot's copy came from: the warm stock, built ahead, or its own build.</summary>
    public static void Acquired(Copy copy, string origin) => _copies.GetOrCreateValue(copy).Origin = origin;

    public static void Fought(PoolJob job, int slot, Copy copy, string target, ulong seed, TargetResult result)
    {
        var seen = _copies.GetOrCreateValue(copy);
        var key = Ids.BriefKey(target);
        var batches = string.Join(',', result.Batches.Select(Hash));
        Log.Info(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Marker}{job.RunId ?? "-"} side={job.Side?.ToString() ?? "-"} slot={slot} copy={seen.Origin} nth={++seen.Fights} after={seen.Last} target={key} seed={seed} state={result.State} batches={batches}"
            )
        );
        seen.Last = key;
    }

    /// <summary>Starts a record: the dump will hold the fights logged from now on.</summary>
    public static void On(string dataRoot) => _from = Length(dataRoot);

    public static void Off() => _from = null;

    public static bool IsOn => _from != null;

    /// <summary>
    /// The fights the host logged since the record was switched on, one per line as logged; null when it is off.
    /// </summary>
    public static IReadOnlyList<string>? Dump(string dataRoot)
    {
        if (_from is not { } from)
            return null;
        var path = Path.Combine(dataRoot, "logs", HostLog);
        if (!File.Exists(path))
            return [];
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // A host that started again begins a new log: read it from its start.
        file.Seek(from <= file.Length ? from : 0, SeekOrigin.Begin);
        using var reader = new StreamReader(file);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var at = line.IndexOf(Marker, StringComparison.Ordinal);
            if (at >= 0)
                lines.Add(line[at..]);
        }
        return lines;
    }

    private static long Length(string dataRoot)
    {
        var path = Path.Combine(dataRoot, "logs", HostLog);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    /// <summary>One short hash over every batch of a result.</summary>
    public static string Hash(TargetResult result) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result.Batches, RunFile.Options)))[
            ..12
        ];

    private static string Hash(Tally batch) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(batch, RunFile.Options)))[..6];
}
