using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// How a climb's step ends: its winner, or the held set, held for the next step.
internal sealed partial class SearchRun
{
    // A climb's step ended: its winner holds.
    private void StepEnded(List<StageResult> results, int trials)
    {
        var climb = _climb!;
        // A step's winner is its best set, unless the held set is as good: it stays; with none inside the death limit
        // yet, the one that died least. A climb in a style current gear doesn't fight in never holds current gear.
        var own = OwnStyle(results);
        var end = Race.End([.. results.Where(r => !own || r.Index != 0).Select(Held)], Request.Goal, Margin, _prices);
        var winner = end.State switch
        {
            AnswerState.Found when end.Best is { } best => Unheld(best),
            AnswerState.NoneWithinLimit when FewestDeaths() is var fewest && !(own && fewest == 0) => fewest,
            _ => own ? results.First(r => r.Index != 0).Index : _held,
        };
        _passes?.Noted(end, Unheld, _candidates, winner);
        _settling.Noted(end, Unheld, _candidates, winner);
        Summarise(trials, _roundSets, 1, _roundClock.Elapsed.TotalSeconds);
        // The weapon step's best set on another weapon, when close, is where a second climb starts: that weapon may
        // lead to more once the slots around it change.
        var weapon = climb.WeaponOf(_candidates[winner].Changes);
        var runnerUp = climb.WeaponStep
            ? Race.RunnerUp(
                [.. results.Select(Held)],
                Held(_last[winner]).Index,
                Request.Goal,
                RestartWithin,
                i => climb.WeaponOf(_candidates[Unheld(i)].Changes) != weapon,
                _prices
            )
            : null;
        climb.Won(
            _candidates[winner].Changes,
            runnerUp is { } second ? _candidates[Unheld(second)].Changes : null,
            _passes?.Order(results, _candidates)
        );
        FinishedStep(winner);
        NextStep();
    }
}
