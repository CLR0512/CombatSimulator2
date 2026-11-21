using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// A gear search's gap from now: once its answer is settled, each of the answer's new items (not owned, or not
/// wearable yet) is swapped alone into current gear with the company it needs (<see cref="GapRows"/>) and fought
/// on the search's own seed and trials, as "Find upgrades" swaps items, so each gain is paired with current gear's
/// result in the answer. The rows go largest gain first, the first being the item to get first.
/// </summary>
internal sealed class GapStage
{
    private readonly ResultQueue _queue = new();
    private readonly Dictionary<int, StageResult> _results = [];
    private IReadOnlyList<GapSwap> _swaps = [];
    private SearchRun? _run;
    private Answer? _answer;
    private GearCatalogue? _catalogue;
    private Action<Answer>? _end;
    private PriceTable? _prices;
    private int _generation = -1;
    private bool _started;

    /// <summary>
    /// Fights the swaps of <paramref name="answer"/>'s new items and calls <paramref name="end"/> with the answer
    /// once done; false when there are none (or not a gear search's answer) and the caller ends it.
    /// </summary>
    public bool Start(Answer answer, SearchRun run, GearCatalogue? catalogue, PriceTable prices, Action<Answer> end)
    {
        if (_started)
        {
            // An answer a stop made while the swaps were fought: it says the gap wasn't.
            if (answer != _answer && run.Request.Kind == SearchKind.BestGear && answer.Gap.Count == 0)
                answer.GapFought = false;
            return false;
        }
        if (
            run.Request.Kind != SearchKind.BestGear
            || catalogue == null
            || answer is not { State: AnswerState.Found, Best: { } best, Current: not null }
            || answer.StoppedAtStage != null
            || run.Cancelled.IsCancellationRequested
            || TargetSpec.FromKey(run.Request.Target).Kind is not (TargetKind.Enemy or TargetKind.Dungeon)
        )
            return false;
        _swaps = GapRows.Swaps(catalogue, best.Changes);
        if (_swaps.Count == 0)
            return false;
        _started = true;
        (_run, _answer, _catalogue, _end, _prices) = (run, answer, catalogue, end, prices);
        var sets = _swaps.Select(
            (swap, i) => new Candidate(i, swap.Changes, "") { Ignoring = catalogue.Locked(swap.Changes) }
        );
        _generation = run.NewGeneration();
        var request = run.Request;
        var job = new SearchJob(
            run.Snapshot,
            run.LoadoutSlot,
            request.Setup.Changes,
            TargetSpec.FromKey(request.Target),
            new DriveOptions(request.Trials, request.Limits ?? new TrialLimits(), request.Setup.IgnoreRequirements),
            run.Seed,
            request.Copies,
            [.. sets]
        );
        Log.Info($"search gap: {_swaps.Count} swaps into current gear, {request.Trials} trials, seed {run.Seed}");
        run.Fighter.Stage(
            job,
            new SearchReport(_queue, _generation, Done, _ => Rows(), Failed),
            _generation,
            run.Cancelled
        );
        return true;
    }

    /// <summary>Main thread: takes in what the stage has sent.</summary>
    public void OnFrame()
    {
        if (_generation >= 0)
            _queue.Drain(_generation);
    }

    private void Done(int index, TargetResult result, Note? left, string? mainSkill) =>
        _results[index] = new StageResult(index, result, Answers.MetricFor(_run!.Request.Goal, mainSkill));

    // Stopped or failed: the answer stands without its gap.
    private void Failed(Exception error)
    {
        Log.Warn("search gap stopped", error);
        _answer!.GapFought = false;
        Finish();
    }

    private void Rows()
    {
        var answer = _answer!;
        var current = answer.Current!;
        if (answer.Best?.Percent is { } gap)
            answer.Gap = GapRows.Rows(
                _catalogue!,
                _swaps,
                new StageResult(-1, current.Result, current.Metric),
                _results,
                gap,
                _run!.Margin,
                _prices
            );
        answer.GapFought = true;
        Finish();
    }

    private void Finish()
    {
        _generation = -1;
        if (_run?.IsActive == true)
            _end!(_answer!);
    }
}
