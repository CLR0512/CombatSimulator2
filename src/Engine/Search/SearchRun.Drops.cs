using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// Droptimizer: the same stages, with each candidate judged against current gear alone.
internal sealed partial class SearchRun
{
    private DropPlan? _drops;
    private int _noGain;
    private int _overLimit;

    /// <summary>
    /// Droptimizer's catalogue is read and its candidates chosen: the stages begin, unless no unowned item can be
    /// worn now.
    /// </summary>
    public void PlannedDrops(DropPlan plan, GearCatalogue catalogue)
    {
        MainThread.Assert("SearchRun.PlannedDrops");
        if (!IsActive)
            return;
        Key = Key with { Gear = catalogue.GearHash, Levels = catalogue.LevelsHash };
        _drops = plan;
        _catalogue = catalogue;
        _candidates = [.. plan.Candidates];
        if (plan.Candidates.Count <= 1)
        {
            var nothing = DropAnswer(AnswerState.NothingToSearch);
            End(nothing);
            return;
        }
        StartRound([.. plan.Candidates.Select(c => c.Index)], held: 0);
    }

    // A stage of Droptimizer has ended: the candidates that can't be gains any more leave, counted by why; after
    // the last, the ones left are judged and the list is made.
    private void DropStageDone(int trials, int fought, List<StageResult> results, bool last)
    {
        if (!last)
        {
            var step = Droptimizer.After(results, Request.Goal, Margin, _prices);
            foreach (var why in step.Dropped.Values)
            {
                if (why == DropReason.OverLimit)
                    _overLimit++;
                else if (why != DropReason.Refused)
                    _noGain++;
            }
            _in = [.. _in.Where(step.In.Contains)];
        }
        Summarise(trials, fought, _in.Count, _clock.Elapsed.TotalSeconds);
        Timed(_fighting, trials, whole: false);
        if (!last)
        {
            NextStage();
            return;
        }
        var answer = DropAnswer(AnswerState.Found);
        Answers.FillDrops(answer, _drops!, results, Request.Goal, Margin, _catalogue!, _prices);
        answer.NoGain += _noGain;
        answer.OverLimit += _overLimit;
        End(answer);
    }

    private Answer DropAnswer(AnswerState state)
    {
        var answer = NewAnswer(state);
        answer.Possible = _drops!.Wearable;
        answer.RuledOut = new Dictionary<string, double> { [SearchPlan.Beaten] = _drops.RuledOut };
        answer.Unowned = _drops.Unowned;
        answer.Wearable = _drops.Wearable;
        answer.NotYetWearable = [.. _drops.NotYetWearable];
        return answer;
    }
}
