using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// What a bug report says the search ran and found.
internal sealed partial class SearchRun
{
    /// <summary>The search's id, as its log lines and a bug report name it.</summary>
    public string Id { get; init; } = "";

    /// <summary>What it was asked and, once it has ended, what it found; null until the start sets it.</summary>
    public SearchRecord? Record { get; init; }

    // A failed search keeps its snapshot, as a failed run does.
    private void Recorded(Answer answer)
    {
        if (Record is not { } record)
            return;
        record.State = answer.State switch
        {
            AnswerState.Cancelled => "cancelled",
            AnswerState.Failed => "failed",
            _ => "finished",
        };
        record.Answer = SearchOutcome.Of(answer);
        if (answer.State != AnswerState.Failed)
            return;
        record.Failure = new SearchFailure(Stage, FailureRecord.Name(FailureKind.Run), _target.Key);
        FailedSnapshots.Keep(Id, _snapshot);
    }
}
