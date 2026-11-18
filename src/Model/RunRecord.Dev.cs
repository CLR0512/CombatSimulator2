using System.Text.Json;

namespace MelvorMods.CombatSimulator2.Model;

// Reading a record back from a report, for reruns.
public sealed partial class RunRecord
{
    /// <summary>
    /// The record in one entry of a report's <c>runs.json</c>; null when the entry has none (reports saved before
    /// records were written). Throws <see cref="JsonException"/> on a damaged record.
    /// </summary>
    public static RunRecord? Read(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("request", out var request)
        && request.ValueKind == JsonValueKind.Object
            ? request.Deserialize<RunRecord>(Options)
            : null;

    /// <summary>What a rerun needs that the record lacks, by field name; empty when it can be rerun.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (this.Targets.Count == 0)
            missing.Add("targets");
        if (Seed == null)
            missing.Add("seed");
        if (string.IsNullOrEmpty(SnapshotSha256))
            missing.Add("snapshotSha256");
        return missing;
    }

    /// <summary>The setup the run ran, without the loadout's name.</summary>
    public Setup RunSetup() =>
        new()
        {
            Character = Setup.Character,
            Loadout = Setup.Loadout,
            IgnoreRequirements = Setup.IgnoreRequirements,
            Changes = Setup.Changes,
        };
}
