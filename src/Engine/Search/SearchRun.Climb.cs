using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// A search that improves a slot at a time: its climbs, one a style when other styles are included, each from
// current gear, then current gear raced against what each ended holding.
internal sealed partial class SearchRun
{
    private ClimbQueue? _climbQueue;
    private readonly List<int> _climbsHeld = [];

    // How far behind the weapon step's winner its runner-up may be and still have a climb of its own.
    private const double RestartWithin = 0.10;

    // Since the style's climbs began (each style has its own), plus the first's gear race.
    private readonly Stopwatch _climbClock = new();
    private double _beforeClimbs;
    private readonly Stopwatch _sinceSearchBegan = Stopwatch.StartNew();

    /// <summary>The seconds left; the bar follows them (see <see cref="SearchProgress.Left"/>).</summary>
    public double? SecondsLeft =>
        _progress.Left(
            _progress.Update(_trials, _roundStage, Left, StageFought, _climb),
            _sinceSearchBegan.Elapsed.TotalSeconds,
            _climb != null,
            _roundClock.Elapsed.TotalSeconds,
            _climb == null ? null : _climbQueue?.Limit.Left(_sinceSearchBegan.Elapsed.TotalSeconds)
        );

    /// <summary>Whether <see cref="SecondsLeft"/> is the time to the player's limit, the most the search has left.</summary>
    public bool LeftIsLimit => _progress.AtLimit;

    /// <summary>How far the whole search has come, from 0 to 1, for one bar over all its stages and steps.</summary>
    public double Progress
    {
        get
        {
            _ = SecondsLeft; // the bar follows the time left, which moves it on
            return _progress.Shown;
        }
    }

    // The stage under way's own cancel, under the search's: a search can stop a stage and go on.
    private CancellationTokenSource? _stageCancel;

    // How long a first stage runs before its pace is read: long enough for every copy to have finished a few sets.
    private const double PaceAfterSeconds = 10;

    // A search that set out to fight every set, whose estimate was wrong: its first stage has run past the
    // long-search limit, or its pace so far says the search would. It goes on a slot at a time instead.
    private bool FirstStageTooLong()
    {
        if (
            !IsActive
            || Stage != 1
            || _climb != null
            || _drops != null
            || _plan is not { Climb: false }
            || _catalogue == null
        )
            return false;
        var seconds = _clock.Elapsed.TotalSeconds;
        var limit = SearchTimes.Limit(Request);
        if (seconds > limit)
            return true;
        if (seconds < Math.Min(PaceAfterSeconds, limit))
            return false;
        var trials = _trials[0];
        var fought = _in.Count(i => _fought.ContainsKey((i, trials)));
        // With none done yet it is read as one done: the fastest pace it could still have.
        var perTrial = seconds / (Math.Max(1, fought) * (double)trials);
        return SearchTimes.Expected(perTrial, _in.Count, Request.Trials) > limit;
    }

    // The stage stops; what it fought so far times the next search's estimate and is kept for the climb's steps,
    // which use a result already in hand rather than fight its set again.
    private void GoSlotAtATime()
    {
        _stageCancel?.Cancel();
        _generation = _nextGeneration();
        var trials = _trials[0];
        var fought = _in.Count(i => _fought.ContainsKey((i, trials)));
        Timed(fought, trials, whole: true);
        Log.Info(
            $"search of {Request.Target}: {fought} of {_in.Count} sets fought in {_clock.Elapsed.TotalSeconds:0} s, too slow to fight them all within {SearchTimes.Limit(Request):0} s: going a slot at a time"
        );
        _plan = Planner.Plan(_catalogue!, Request with { SlotAtATime = true });
        _changed = true;
        StartClimbs(_plan, _catalogue!);
    }

    // A setup search whose gear was raced whole climbs over its choices from the race's winner, given as from.
    private void StartClimbs(SearchPlan plan, GearCatalogue catalogue, Climb? from = null)
    {
        _climbQueue ??= new ClimbQueue(Request, _passes?.Steps);
        _climb = _climbQueue.Start(plan, catalogue, from, _sinceSearchBegan.Elapsed.TotalSeconds);
        _progress.ClimbStarts(_climbQueue.After);
        _beforeClimbs = _sinceSearchBegan.Elapsed.TotalSeconds;
        _climbClock.Restart();
        NextStep();
    }

    // A climb's next step, or the answer when none is left: current gear against the sets held at the end.
    private void NextStep()
    {
        Debug.Assert(_climb != null);
        // A climb runs until a pass changes nothing or its last pass ends, or until its share of the limit is used.
        var elapsed = _sinceSearchBegan.Elapsed.TotalSeconds;
        var budget = _climbQueue!.Limit;
        var stopped = budget.Over(elapsed);
        if (!stopped && _climb.Next() is { } sets)
        {
            StartRound([.. sets.Select(IndexOf)], IndexOf(_climb.Held));
            return;
        }
        _climbsHeld.Add(IndexOf(_climb.Held));
        Log.Info(
            $"search climb{(_climb.Restarted ? " from the runner-up" : "")} {(stopped ? "stopped at the limit" : "ended")} in {_climb.Passes} passes, {_climbClock.Elapsed.TotalSeconds + _beforeClimbs:0} s, holding {(_climb.Held.IsEmpty ? "current gear" : _climb.Held.ShortForm)}"
        );
        var trials = _trials[^1];
        var final = _last.ToDictionary(r => r.Key, r => _fought.GetValueOrDefault((r.Key, trials), r.Value));
        var results = _climbsHeld.Prepend(0).Distinct().Where(final.ContainsKey).Select(i => final[i]).ToList();
        // The runner-up's climb when there is room for it, else the next style's.
        var next =
            (!stopped && budget.RoomForRestart(elapsed) ? _climb.Restart() : null) ?? _climbQueue.NextStyle(elapsed);
        if (next != null)
        {
            _climb = next;
            _progress.ClimbStarts(_climbQueue.After);
            _beforeClimbs = 0;
            _climbClock.Restart();
            NextStep();
            return;
        }
        var answer = NewAnswer(AnswerState.Found);
        answer.StoppedAtLimit = budget.Stopped ? Request.MaxSeconds : null;
        Answers.Fill(answer, _candidates, results, final, Request.Goal, Margin, Request.AllStyles, _catalogue, _prices);
        if (_passes?.Races.Start(answer, this, _catalogue, End) != true)
            End(answer);
    }

    // A set's place among the candidates, added when a step offers one for the first time.
    private int IndexOf(ChangeList changes)
    {
        var index = _candidates.FindIndex(c => c.Changes.Equals(changes));
        if (index >= 0)
            return index;
        _candidates.Add(_climb?.CandidateOf(_candidates.Count, changes) ?? new(_candidates.Count, changes, ""));
        return _candidates.Count - 1;
    }
}
