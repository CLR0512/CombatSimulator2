using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// One round raced with every set fought once: the held set first, to the round's last stage, read at the stages
/// below it; then every other set in one fight each, read at the same counts and stopped where it is clearly behind
/// the held set there. Today's stages fight a set again from its first trial at each count; here a check costs no
/// fight, and its result is the one that stage would have read. Keeps which sets left where, so the stage summaries
/// read as a staged round's do.
/// </summary>
internal sealed class RaceChecks(IReadOnlyList<int> stages, int held)
{
    private readonly Dictionary<int, (int Check, DropReason Why)> _left = [];
    private CheckRule? _rule;

    /// <summary>The counts the sets are read at: the round's stages below its last.</summary>
    public IReadOnlyList<int> At { get; } = [.. stages.SkipLast(1)];

    /// <summary>The round's trials: its last stage.</summary>
    public int Trials { get; } = stages[^1];

    public int Held { get; } = held;

    /// <summary>The trials the round fought, over every set.</summary>
    public long Fought { get; private set; }

    /// <summary>
    /// Whether a round races this way: a step of a climb or Droptimizer's stages, on one target, with a stage to
    /// check before the last. A search that fights every set keeps its stages, which time its switch to a slot at a
    /// time; so do several targets fought in turn, and other styles raced apart.
    /// </summary>
    public static bool Apply(TargetKind kind, bool stepOrCandidates, bool allStyles, int stageCount) =>
        stepOrCandidates && !allStyles && stageCount >= 2 && kind is not (TargetKind.Master or TargetKind.Set);

    /// <summary>How the held set is fought: read at every check and never stopped.</summary>
    public CheckRule Reading(Goal goal, double share) => new(At, [], "", goal, share);

    /// <summary>
    /// The rule the other sets fight under, from the held set's results at the checks (fewer when its fight ended
    /// early: a set is never stopped past them), measured in <paramref name="heldMetric"/>: the race's own, or with
    /// <paramref name="candidates"/> Droptimizer's against current gear.
    /// </summary>
    public CheckRule Rule(
        IReadOnlyList<TargetResult> heldAt,
        string heldMetric,
        Goal goal,
        double share,
        bool candidates,
        (string? Skill, string Style)? current,
        PriceTable prices
    ) =>
        _rule = new(
            At,
            heldAt,
            heldMetric,
            goal,
            share,
            candidates,
            current?.Skill,
            current?.Style,
            Checks.NeedsPrices(goal) ? prices.All : null
        );

    /// <summary>
    /// A set's fight came back, measured in <paramref name="metric"/>: its results at the counts it was read at, each
    /// with its count, its result, and whether it is still in at the end. A set stopped at a check ends with its
    /// result there; any other ends with its fight's, at the round's trials, as a staged round's last stage keeps it.
    /// </summary>
    public (List<(int Count, TargetResult Result)> Read, TargetResult Result, bool In) Took(
        int index,
        TargetResult fought,
        string metric
    )
    {
        var read = new List<(int, TargetResult)>();
        var passed = fought.AtChecks ?? [];
        fought.AtChecks = null;
        for (var i = 0; i < passed.Count && i < At.Count; i++)
            read.Add((At[i], passed[i]));
        if (fought.State == TargetState.Fighting && passed.Count > 0)
        {
            var at = passed[^1];
            Fought += at.TrialsDone;
            var check = Math.Min(passed.Count, At.Count) - 1;
            _left[index] = (check, (_rule == null ? null : Checks.At(_rule, check, at, metric)) ?? DropReason.Behind);
            return (read, at, false);
        }
        Fought += fought.State == TargetState.Refused ? 0 : fought.TrialsDone;
        read.Add((Trials, fought));
        return (read, fought, true);
    }

    /// <summary>
    /// Each check as a staged round's stage reads: its count, the sets that reached it (<paramref name="before"/>
    /// came in, the held set and those answered from earlier results among them), and those left after it.
    /// </summary>
    public IEnumerable<(int Trials, int Fought, int Left)> Summaries(int before)
    {
        var alive = before;
        for (var i = 0; i < At.Count; i++)
        {
            var leaving = _left.Values.Count(l => l.Check == i);
            yield return (At[i], alive, alive - leaving);
            alive -= leaving;
        }
    }

    /// <summary>Why each set that left did.</summary>
    public IEnumerable<DropReason> Reasons => _left.Values.Select(l => l.Why);
}
