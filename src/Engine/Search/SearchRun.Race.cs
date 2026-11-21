using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// How a round's sets are raced: the held set first, each style apart when other styles are included, and the
// leaders fought again when the best came from behind.
internal sealed partial class SearchRun
{
    // The round's set closest to the death limit by each set's last result; the held set when equal.
    private int FewestDeaths() =>
        DeathLimits.Fewest(_roundAll, _held, _last, Ranking.LowerIsBetter(Request.Goal.Metric), _prices);

    // A best that was behind the leader by more than the margin at an earlier stage got ahead later: the last stage
    // is fought again with a second seed for current gear, the best and its ties. True when the fight was started.
    private bool FightLeadersAgain(List<StageResult> results)
    {
        if (_behindAt != null || Request.AllStyles || _trials.Count < 2)
            return false;
        var end = Race.End([.. results.Select(Held)], Request.Goal, Margin, _prices);
        if (
            end is not { State: AnswerState.Found, Best: { } best }
            || Race.BehindAt(_steps, best, Margin, from: _trials[0] == Answers.FirstRung ? 1 : 0) is not { } stage
        )
            return false;
        _behindAt = stage;
        // A seed of its own, kept under 2^53 so it reads back exactly wherever it is written.
        Seed = (Seed * 6364136223846793005UL + 1442695040888963407UL) >> 11;
        _in = [.. new[] { 0, best }.Concat(end.Ties.Select(t => t.Index)).Select(Unheld).Distinct()];
        foreach (var set in _in)
            _fought.Remove((set, _trials[^1]));
        Log.Info($"the best was behind at stage {stage}: {_in.Count} sets fight the last stage again with seed {Seed}");
        _roundStage = _trials.Count - 1;
        NextStage();
        return true;
    }

    // The race takes the set the others must beat as its first: inside a round the held set stands there.
    private StageResult Held(StageResult set) => set with { Index = set.Index == _held ? 0 : set.Index + 1 };

    private int Unheld(int index) => index == 0 ? _held : index - 1;

    // A climb's weapon step in a style current gear doesn't fight in: none of its sets is dropped for losing to it.
    private bool OwnStyle(List<StageResult> results) =>
        Request.AllStyles && _climb is { WeaponStep: true } && _held == 0 && results.Exists(r => Foreign(r.Index));

    private bool Foreign(int index) => index != 0 && _candidates[index].Style != _candidates[0].Style;

    // With several styles each is raced on its own, so each keeps a best; current gear stays in its style's group.
    private IEnumerable<IReadOnlyList<StageResult>> Groups(List<StageResult> results)
    {
        if (!Request.AllStyles || _climb != null)
            return [results];
        var style = _candidates.ToDictionary(c => c.Index, c => c.Style);
        return results.GroupBy(r => style[r.Index]).Select(g => (IReadOnlyList<StageResult>)[.. g]);
    }
}
