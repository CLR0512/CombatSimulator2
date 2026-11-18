using System.Text.Json.Serialization;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Host;

// A search's messages: one stage of candidates out, one result per candidate back.

/// <summary>
/// One set of a stage: its place in the search and its changes on the base, in short form, with those whose
/// requirements are ignored (things the character doesn't have that a setup search includes).
/// </summary>
internal sealed record SearchCandidate(
    int Index,
    string Changes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Ignoring = null
);

/// <summary>
/// One stage of a search for the host: the base as a run's start has it, one target, the stage's trials in
/// <c>Options</c>, and the candidates. The host keeps the copies it prepared for <c>SearchId</c> between stages, so
/// only the first stage sends the snapshot, in its own frame, with <c>SnapshotFollows</c> set; a host that no
/// longer holds it answers <c>Failed</c> with <see cref="SnapshotGone"/> and is sent the stage again with it.
/// <c>OwnLoadouts</c> fights a master's monsters with a loadout of their own, which the candidates leave out.
/// <c>Checks</c> makes the stage a race fought once: each set fights to the trials once, is read at the checks and
/// stops where the rule drops it, its results at the checks it passed coming back with its result.
/// </summary>
internal sealed record SearchStartMessage(
    int Generation,
    string SearchId,
    string Character,
    string Sha256,
    string Build,
    string? LoadoutSlot,
    string Target,
    RunOptions Options,
    ulong Seed,
    int Copies,
    long ReserveBytes,
    List<SearchCandidate> Candidates,
    bool SnapshotFollows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Changes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OwnLoadouts = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CheckRule? Checks = null
)
{
    /// <summary>What a host says when a later stage names a snapshot it dropped.</summary>
    public const string SnapshotGone = "snapshot gone";
}

/// <summary>
/// One candidate's result of the stage; each candidate gets exactly one, in any order. <c>Left</c> says why a
/// candidate wasn't fought (the game refused its gear, or the target), beside a refused result. <c>MainSkill</c> is
/// the skill the set trains.
/// </summary>
internal sealed record CandidateDoneMessage(
    int Generation,
    int Index,
    TargetResult Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Note? Left = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MainSkill = null
);

/// <summary>The search is over: drop its copies.</summary>
internal sealed record SearchEndMessage(string SearchId);

/// <summary>
/// Plan a search on the host, followed by its snapshot: the host reads the catalogue on a copy of its own, so the
/// game builds none, and keeps the snapshot for <c>SearchId</c>'s stages. <c>PerTrial</c> is how long a trial of
/// this kind of target took the last search, which decides whether every set can be fought in time.
/// </summary>
internal sealed record SearchPlanMessage(
    int Generation,
    string SearchId,
    string Character,
    string Sha256,
    string Build,
    string? LoadoutSlot,
    SearchRequest Request,
    PlanWhat What,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? PerTrial = null
);

/// <summary>The plan the host read; a plan that failed is a <c>Failed</c> message instead.</summary>
internal sealed record SearchPlannedMessage(int Generation, PlanReply Reply);
