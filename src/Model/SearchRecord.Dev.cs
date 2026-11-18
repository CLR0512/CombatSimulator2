using System.Text.Json;

namespace MelvorMods.CombatSimulator2.Model;

// Reading records back from a report, for reruns.
public sealed partial class SearchRecord
{
    /// <summary>
    /// The records in a report's <c>searches.json</c>; empty when it has none (reports saved before they were
    /// written). Throws <see cref="JsonException"/> on a damaged record.
    /// </summary>
    public static IReadOnlyList<SearchRecord> Read(JsonElement searches) =>
        searches.ValueKind == JsonValueKind.Object
        && searches.TryGetProperty("searches", out var list)
        && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(e => e.Deserialize<SearchRecord>(Options)!)]
            : [];

    /// <summary>What a rerun needs that the record lacks, by field name; empty when it can be rerun.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(Target))
            missing.Add("target");
        if (Seed == null)
            missing.Add("seed");
        if (string.IsNullOrEmpty(SnapshotSha256))
            missing.Add("snapshotSha256");
        return missing;
    }
}
