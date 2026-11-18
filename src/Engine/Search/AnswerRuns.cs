using System.Text.Json;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// A set of a search's answer as the run it is: the setup with the set's changes, the one target, the search's
/// seed and trials, and the set's result of the last stage. Nothing is fought to make it.
/// </summary>
internal static class AnswerRuns
{
    /// <summary>The setup a set fights with: the search's base with the set's changes over its own.</summary>
    public static Setup SetupOf(SearchRun search, SetResult set)
    {
        var from = search.Request.Setup;
        var changes = from.Changes ?? ChangeList.Empty;
        foreach (var change in set.Changes)
            changes = changes.With(change);
        return new Setup
        {
            Character = from.Character,
            Loadout = from.Loadout,
            LoadoutName = from.LoadoutName,
            IgnoreRequirements = from.IgnoreRequirements || set.NeedsRequirementsIgnored,
            Changes = changes,
        };
    }

    /// <summary>The finished run of <paramref name="set"/>, under <paramref name="id"/>.</summary>
    public static Run Of(SearchRun search, SetResult set, string id)
    {
        var target = TargetSpec.FromKey(search.Request.Target);
        var request = new RunRequest(
            SetupOf(search, set),
            [target],
            search.Request.Trials,
            search.Seed,
            search.Request.Copies,
            Limits: search.Request.Limits
        );
        var run = RunPlan.NewRun(request, search.Snapshot, id, search.Seed, search.Where, _ => search.TargetName);
        // The result is the run's own from here on: what the answer holds stays as it was.
        var result = JsonSerializer.Deserialize<TargetResult>(
            JsonSerializer.SerializeToUtf8Bytes(set.Result, RunFile.Options),
            RunFile.Options
        )!;
        result.Name = search.TargetName;
        run.Results[target.Key] = result;
        run.SetupHash = search.SetupHash;
        run.Cs2Mvid = RunService.Cs2Mvid;
        run.SeedSet = true;
        run.MainSkill = set.Metric.StartsWith("xp:", StringComparison.Ordinal) ? set.Metric[3..] : null;
        run.State = RunState.Finished;
        run.EndedAt = DateTime.UtcNow;
        return run;
    }

    /// <summary>
    /// What starts the comparison of current gear against <paramref name="set"/>: side A's request, the search's
    /// base. Both sets' results are first put in <paramref name="index"/> as runs of their own, so each side is
    /// answered from there where its setup comes out the same.
    /// </summary>
    public static RunRequest Comparison(
        SearchRun search,
        SetResult set,
        IEnumerable<string> knownIds,
        ResultIndex? index
    )
    {
        var known = knownIds.ToList();
        foreach (var side in new[] { search.Answer!.Current, set }.OfType<SetResult>())
        {
            var run = Of(search, side, RunIds.Next(DateTime.Now, known));
            known.Add(run.Id);
            index?.Add(run);
        }
        // A run fights targets, not sets: an area's or the pins' answer is run on each of its members.
        var target = TargetSpec.FromKey(search.Request.Target);
        return new RunRequest(
            search.Request.Setup,
            target.Kind == TargetKind.Set ? [.. Targets.Members(LiveGame.Require(), target)] : [target],
            search.Request.Trials,
            search.Seed,
            search.Request.Copies,
            search.Where,
            Limits: search.Request.Limits
        );
    }

    /// <summary>
    /// The set of <paramref name="answer"/> a command or the panel names: the best (current gear when the answer
    /// is to keep it) without <paramref name="tie"/>, else the tie of that number, from 1; for Droptimizer, the
    /// gain of <paramref name="item"/>; or a setup search's Cheaper or Safer, as <paramref name="alternative"/> names it.
    /// </summary>
    public static SetResult Pick(Answer answer, int? tie, string? item = null, string? alternative = null)
    {
        if (alternative != null)
            return alternative switch
            {
                "cheaper" when answer.Alternatives?.Cheaper is { } cheaper => cheaper,
                "safer" when answer.Alternatives?.Safer is { } safer => safer,
                _ => throw new CommandException($"the answer has no {alternative} setup"),
            };
        if (item != null)
            return answer.Gains.Concat(answer.Safer).FirstOrDefault(g => g.Item == item) is { } gain
                ? new SetResult
                {
                    Changes = gain.Changes,
                    Result = gain.Result,
                    Metric = gain.Metric,
                }
                : throw new CommandException($"{Ids.Brief(item)} is not one of the answer's gains");
        if (tie is { } n)
            return n >= 1 && n <= answer.Ties.Count
                ? answer.Ties[n - 1]
                : throw new CommandException($"tie={n}: the answer has {answer.Ties.Count} sets as good");
        return answer.Best
            ?? (answer.State == AnswerState.KeepCurrent ? answer.Current : null)
            ?? throw new CommandException("the answer has no set to take");
    }
}
