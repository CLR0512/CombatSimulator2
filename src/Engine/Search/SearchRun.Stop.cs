using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// A stopped search keeps the best set it had: the leader of the last stage every set left finished, or of a climb,
// the best of its last finished step against current gear.
internal sealed partial class SearchRun
{
    private (List<StageResult> Results, int Stage)? _finished;

    /// <summary>The results a stop now would keep, with the stage they finished at; null before any.</summary>
    public (List<StageResult> Results, int Stage)? FinishedSoFar => _finished;

    // A whole search's stage just finished: its sets' results, the dropped ones among them, are what a stop keeps.
    private void Finished(List<StageResult> results) => _finished = (results, _stages.Count);

    // A climb's step just finished: its winner against current gear is what a stop keeps.
    private void FinishedStep(int winner)
    {
        if (!_last.TryGetValue(0, out var current) || !_last.TryGetValue(winner, out var best))
            return;
        _finished = (winner == 0 ? [current] : [current, best], _stages.Count);
    }

    // The answer of a search stopped now: the best so far, marked with the stage it stopped at, or nothing kept.
    private Answer Stopped()
    {
        var answer = NewAnswer(AnswerState.Cancelled);
        answer.CancelledAfter = _stages.Count;
        if (_drops != null || _finished is not { } done || !_last.ContainsKey(0))
            return answer;
        Answers.Fill(
            answer,
            _candidates,
            done.Results,
            _last,
            Request.Goal,
            Margin,
            Request.AllStyles && _climb == null,
            _catalogue,
            _prices
        );
        answer.StoppedAtStage = done.Stage;
        answer.CancelledAfter = null;
        return answer;
    }
}
