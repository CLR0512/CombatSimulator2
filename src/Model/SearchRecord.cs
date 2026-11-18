using System.Text.Json;
using System.Text.Json.Serialization;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What a bug report says a gear search or a Droptimizer ran and found: with its snapshot, enough to search again
/// and compare the answers.
/// </summary>
public sealed partial class SearchRecord
{
    /// <summary>The search's id, as its log lines name it.</summary>
    public string Id { get; set; } = "";

    /// <summary><c>search</c> or <c>droptimizer</c>.</summary>
    public string Kind { get; set; } = "search";
    public string Target { get; set; } = "";
    public Goal Goal { get; set; } = new(Metrics.MainXp);
    public List<string> Locks { get; set; } = [];
    public List<string> Excluded { get; set; } = [];
    public bool AllStyles { get; set; }
    public double Share { get; set; }

    /// <summary>The last stage's trials.</summary>
    public int Trials { get; set; }

    /// <summary>Null only in a record that lacks it.</summary>
    public ulong? Seed { get; set; }
    public int Copies { get; set; }

    /// <summary><c>host</c> or <c>game</c>.</summary>
    public string? Where { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TrialLimits? Limits { get; set; }
    public bool SlotAtATime { get; set; }
    public RecordedSetup Setup { get; set; } = new();
    public string SnapshotSha256 { get; set; } = "";
    public string? SetupHash { get; set; }
    public string GameBuild { get; set; } = "";
    public string? Cs2Mvid { get; set; }

    /// <summary><c>finished</c>, <c>cancelled</c> or <c>failed</c>.</summary>
    public string State { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SearchOutcome? Answer { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SearchFailure? Failure { get; set; }

    /// <summary>The search's own snapshot's entry in the report, when it carries one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snapshot { get; set; }

    public static JsonSerializerOptions Options => RunFile.Options;

    public static string KindName(SearchKind kind) => kind == SearchKind.Droptimizer ? "droptimizer" : "search";

    /// <summary>The record of <paramref name="request"/>, without its answer yet.</summary>
    public static SearchRecord Of(
        string id,
        SearchRequest request,
        double share,
        ulong seed,
        string where,
        string snapshotSha256,
        string? setupHash,
        string gameBuild,
        string? cs2Mvid
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SearchRecord
        {
            Id = id,
            Kind = KindName(request.Kind),
            Target = request.Target,
            Goal = request.Goal,
            Locks = [.. request.Locks.Order(StringComparer.Ordinal)],
            Excluded = [.. request.Excluded.Order(StringComparer.Ordinal)],
            AllStyles = request.AllStyles,
            Share = share,
            Trials = request.Trials,
            Seed = seed,
            Copies = request.Copies,
            Where = where,
            Limits = request.Limits,
            SlotAtATime = request.SlotAtATime,
            Setup = new RecordedSetup
            {
                Character = request.Setup.Character,
                Loadout = request.Setup.Loadout,
                IgnoreRequirements = request.Setup.IgnoreRequirements,
                Changes = request.Setup.Changes,
            },
            SnapshotSha256 = snapshotSha256,
            SetupHash = setupHash,
            GameBuild = gameBuild,
            Cs2Mvid = cs2Mvid,
        };
    }

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, Options);
}

/// <summary>
/// What a search found, as two searches are compared: the best set and the ties by their changes, and each stage's
/// counts.
/// </summary>
public sealed class SearchOutcome
{
    public string State { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Best { get; set; }
    public List<string> Ties { get; set; } = [];

    /// <summary>Each stage as <c>fought/dropped/left</c>.</summary>
    public List<string> Stages { get; set; } = [];

    public static SearchOutcome Of(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new SearchOutcome
        {
            State = answer.State.ToString(),
            Best = answer.Best?.Changes.ShortForm,
            Ties = [.. answer.Ties.Select(t => t.Changes.ShortForm)],
            Stages = [.. answer.Stages.Select(s => $"{s.Fought}/{s.Dropped}/{s.Left}")],
        };
    }
}

/// <summary>Where a search failed: the stage (0 before the first), the failure kind's name and the target.</summary>
public sealed record SearchFailure(int Stage, string Kind, string? Target);
