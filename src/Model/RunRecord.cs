using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What a bug report says a run ran: with the run's snapshot, enough to fight it again and get the same results.
/// Typed names (the run's and the loadout's) are never part of it.
/// </summary>
public sealed partial class RunRecord
{
    public string Id { get; set; } = "";

    /// <summary><c>run</c>, <c>what-if</c>, <c>comparison A</c> or <c>comparison B</c>.</summary>
    public string Kind { get; set; } = "run";

    /// <summary>The other side's run id, for a comparison's side.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PairedWith { get; set; }

    /// <summary>Target keys in the run's order.</summary>
    public List<string> Targets { get; set; } = [];
    public int Trials { get; set; }
    public int Copies { get; set; }

    /// <summary><c>host</c> or <c>game</c>.</summary>
    public string? Where { get; set; }
    public TrialLimits Limits { get; set; } = new();

    /// <summary>Null only in a record that lacks it.</summary>
    public ulong? Seed { get; set; }

    /// <summary>Whether the seed was given rather than drawn.</summary>
    public bool SeedSet { get; set; }

    /// <summary>
    /// Each target's seed where it isn't <see cref="Seed"/>: a comparison's B side takes A's, and a reused result
    /// keeps the seed of the run it was first fought in. A rerun fights every target fresh on these.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ulong>? TargetSeeds { get; set; }
    public bool Fresh { get; set; }
    public double? StopPrecision { get; set; }
    public string? MainSkill { get; set; }
    public RecordedSetup Setup { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChangeOutcome>? ChangeOutcomes { get; set; }
    public string SnapshotSha256 { get; set; } = "";
    public string? SetupHash { get; set; }
    public string GameBuild { get; set; } = "";

    /// <summary>False when the run was made on a game build CS2 wasn't checked with; left out when it was.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Checked { get; set; }
    public string? Cs2Mvid { get; set; }
    public string State { get; set; } = "";

    /// <summary>The short results hash, as <c>cs2 result</c> prints it; only for a finished run.</summary>
    public string? ResultHash { get; set; }

    /// <summary>The run's own snapshot's entry in the report, when it carries one.</summary>
    public string? Snapshot { get; set; }

    /// <summary>The camel-case form run files use.</summary>
    public static JsonSerializerOptions Options => RunFile.Options;

    /// <summary>
    /// The record of <paramref name="run"/>. <paramref name="targetSeeds"/> is every target's seed (see
    /// <see cref="PairSeeds.Of"/>); only those differing from the run's seed are kept.
    /// </summary>
    public static RunRecord From(
        Run run,
        string kind,
        string? pairedWith,
        IReadOnlyDictionary<string, ulong> targetSeeds,
        string? resultHash,
        string? snapshotFile
    )
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(targetSeeds);
        var differing = targetSeeds
            .Where(s => s.Value != run.Seed)
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        return new RunRecord
        {
            Id = run.Id,
            Kind = kind,
            PairedWith = pairedWith,
            Targets = [.. run.Targets],
            Trials = run.Trials,
            Copies = run.Copies,
            Where = run.SimulatedIn,
            Limits = new TrialLimits
            {
                EnemySeconds = run.Limits.EnemySeconds,
                DungeonSeconds = run.Limits.DungeonSeconds,
                MasterPartSeconds = run.Limits.MasterPartSeconds,
            },
            Seed = run.Seed,
            SeedSet = run.SeedSet,
            TargetSeeds = differing.Count > 0 ? differing : null,
            Fresh = run.Fresh,
            StopPrecision = run.StopPrecision,
            MainSkill = run.MainSkill,
            Setup = new RecordedSetup
            {
                Character = run.Setup.Character,
                Loadout = run.Setup.Loadout,
                IgnoreRequirements = run.Setup.IgnoreRequirements,
                Changes = run.Setup.Changes,
            },
            ChangeOutcomes = run.ChangeOutcomes,
            SnapshotSha256 = run.SnapshotSha256,
            SetupHash = run.SetupHash,
            GameBuild = run.GameBuild,
            Checked = run.Checked,
            Cs2Mvid = run.Cs2Mvid,
            State = run.State.ToString(),
            ResultHash = run.State == RunState.Finished ? resultHash : null,
            Snapshot = snapshotFile,
        };
    }

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, Options);
}

/// <summary>A run's setup without the loadout's name: the slot id is enough to rerun it.</summary>
public sealed class RecordedSetup
{
    public string Character { get; set; } = "";

    /// <summary><c>current</c>, or a loadout slot id.</summary>
    public string Loadout { get; set; } = Model.Setup.Current;
    public bool IgnoreRequirements { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChangeList? Changes { get; set; }
}
