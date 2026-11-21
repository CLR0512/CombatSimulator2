using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>What fights a search's stages: a pool in this process, or the simulator host.</summary>
internal interface ISearchFighter : IDisposable
{
    /// <summary>Starts one stage and returns at once; the report is called on worker threads.</summary>
    void Stage(SearchJob job, ISearchReport report, int generation, CancellationToken token);

    /// <summary>
    /// Plans in a process of the fighter's own and returns at once, <paramref name="done"/> running on the main
    /// thread; false when the fighter has none, and the game plans on its own check copy.
    /// </summary>
    bool Plan(SearchPlanJob job, Action<PlanReply?, Exception?> done);
}

/// <summary>
/// A plan to read: the setup's snapshot and loadout, the request, what of it, the generation it goes by, and the
/// search's cancel, which stops it.
/// </summary>
internal sealed record SearchPlanJob(
    Snapshot Snapshot,
    string? LoadoutSlot,
    SearchRequest Request,
    PlanWhat What,
    double? PerTrial,
    int Generation,
    CancellationToken Token
);

/// <summary>A search's stages fought in this process.</summary>
internal sealed class LocalSearchFighter : ISearchFighter
{
    private readonly SearchPool _pool = new();

    public void Stage(SearchJob job, ISearchReport report, int generation, CancellationToken token) =>
        _pool.Start(job, report, token);

    public bool Plan(SearchPlanJob job, Action<PlanReply?, Exception?> done) => false;

    public void Dispose() => _pool.Dispose();
}

/// <summary>
/// One search from its candidates to its answer, on the main thread: the sets still in fight a stage, the race
/// drops those clearly behind, the rest fight the next, and the last stage's results make the answer. Candidates'
/// results live here only: nothing is a saved run. Worker output arrives through a queue tagged with the search's
/// generation, so what a cancelled stage still sends is dropped.
/// </summary>
internal sealed partial class SearchRun : IDisposable
{
    private readonly ResultQueue _queue = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly ISearchFighter _fighter;
    private readonly Snapshot _snapshot;
    private readonly string? _loadoutSlot;
    private readonly TargetSpec _target;
    private readonly PriceTable _prices;
    private IReadOnlyList<int> _trials;
    private readonly Dictionary<int, StageResult> _last = [];
    private readonly List<StageSummary> _stages = [];
    private readonly List<RaceStep> _steps = [];
    private readonly List<LeftOut> _leftOut = [];
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<(int Index, int Trials), StageResult> _fought = [];
    private readonly Stopwatch _roundClock = new();
    private List<Candidate> _candidates = [];
    private List<int> _in = [];
    private Climb? _climb;

    private int _held;
    private int _roundStage;
    private int _roundSets;
    private readonly SearchProgress _progress = new();
    private int? _behindAt;
    private List<int> _roundAll = [];
    private GearCatalogue? _catalogue;
    private SearchPlan? _plan;
    private SetupPasses? _passes;
    private readonly SettlingRaces _settling = new();
    private readonly GapStage _gap = new();
    private readonly Func<int> _nextGeneration;
    private int _generation;
    private bool _changed;

    public SearchRun(
        SearchRequest request,
        Snapshot snapshot,
        string? loadoutSlot,
        TargetSpec target,
        string targetName,
        ulong seed,
        AnswerKey key,
        ISearchFighter fighter,
        PriceTable prices,
        Func<int> nextGeneration
    )
    {
        _nextGeneration = nextGeneration;
        Request = request;
        Seed = seed;
        Key = key with { Seed = seed };
        TargetName = targetName;
        _snapshot = snapshot;
        _loadoutSlot = loadoutSlot;
        _target = target;
        _fighter = fighter;
        _prices = prices;
        _trials = Answers.Stages(request.Trials);
    }

    public SearchRequest Request { get; }

    /// <summary>Where the stages fight: <c>host</c> or <c>game</c>.</summary>
    public string Where { get; init; } = "";

    /// <summary>The live game's setup hash when the search started, which a run of one of its sets carries.</summary>
    public string? SetupHash { get; init; }

    /// <summary>The seed the results shown were fought with: the search's own, or the second after a rerun.</summary>
    public ulong Seed { get; private set; }
    public AnswerKey Key { get; private set; }
    public string TargetName { get; }

    /// <summary>Whether the search is still planning or fighting.</summary>
    public bool IsActive => Answer == null;

    /// <summary>The stage being fought, from 1; 0 before the first.</summary>
    public int Stage { get; private set; }

    /// <summary>The sets still in.</summary>
    public int Left => _in.Count;

    /// <summary>The trials each set fights in the stage under way; 0 before the first.</summary>
    public int StageTrials => _roundStage == 0 ? 0 : _trials[_roundStage - 1];

    /// <summary>How many of the sets still in have fought the stage under way.</summary>
    public int StageFought => StageTrials == 0 ? 0 : _in.Count(i => _fought.ContainsKey((i, StageTrials)));

    /// <summary>The stages that have ended.</summary>
    public IReadOnlyList<StageSummary> Stages => _stages;

    public IReadOnlyList<Candidate> Candidates => _candidates;

    /// <summary>The last result of every set fought, by its index.</summary>
    public IReadOnlyDictionary<int, StageResult> Results => _last;

    /// <summary>The answer, once the search has ended in any way.</summary>
    public Answer? Answer { get; private set; }

    /// <summary>Raised on the main thread at most once per <see cref="OnFrame"/>, after a stage or the search ends.</summary>
    public event Action? Changed;

    /// <summary>Raised on the main thread with the answer when the search ends.</summary>
    public event Action<Answer>? Ended;

    /// <summary>
    /// Starts fighting <paramref name="candidates"/>, current gear first. <paramref name="plan"/> and
    /// <paramref name="catalogue"/> give the answer its counts and names; without them it has neither.
    /// </summary>
    public void Begin(IReadOnlyList<Candidate> candidates, SearchPlan? plan = null, GearCatalogue? catalogue = null)
    {
        MainThread.Assert("SearchRun.Begin");
        _candidates = [.. candidates];
        _plan = plan;
        _catalogue = catalogue;
        if (plan?.LeftOut is { } left)
            _leftOut.AddRange(left);
        if (plan is { Climb: true } && catalogue != null)
            StartClimbs(plan, catalogue);
        else
            StartRound([.. candidates.Select(c => c.Index)], held: 0);
    }

    /// <summary>What fights the search, which plans it too when it can.</summary>
    public ISearchFighter Fighter => _fighter;

    /// <summary>A generation of the search's own counter, for a plan the host is asked for.</summary>
    public int NewGeneration() => _nextGeneration();

    /// <summary>The snapshot the search fights with, taken when it started.</summary>
    public Snapshot Snapshot => _snapshot;

    public string? LoadoutSlot => _loadoutSlot;

    /// <summary>What the search will fight, once it is planned.</summary>
    public SearchPlan? Plan => _plan;

    /// <summary>
    /// The catalogue is read and the plan made: the answer's key takes the gear and levels it was planned under,
    /// and the stages begin, unless nothing but current gear is left to try.
    /// </summary>
    public void Planned(SearchPlan plan, GearCatalogue catalogue, PlanReply? reply = null)
    {
        MainThread.Assert("SearchRun.Planned");
        if (!IsActive)
            return;
        Key = Key with { Gear = catalogue.GearHash, Levels = catalogue.LevelsHash, Supplies = reply?.Supplies ?? "" };
        _passes = SetupPasses.For(Request, reply?.Choices ?? [], Margin, _prices);
        if (plan.NothingToSearch && _passes == null)
        {
            _plan = plan;
            _candidates = [.. plan.Candidates];
            End(NewAnswer(AnswerState.NothingToSearch));
            return;
        }
        Begin(plan.Candidates, plan, catalogue);
    }

    /// <summary>The search couldn't be planned: it ends as failed with <paramref name="reason"/>.</summary>
    public void PlanFailed(Note reason)
    {
        MainThread.Assert("SearchRun.PlanFailed");
        if (!IsActive)
            return;
        var answer = NewAnswer(AnswerState.Failed);
        answer.Reason = reason;
        End(answer);
    }

    /// <summary>Frame callback: applies worker output.</summary>
    public void OnFrame()
    {
        // A fault while a stage's results are taken in would lose them and leave the search waiting for ever: it
        // ends the search as failed, with the fault as its reason.
        try
        {
            _passes?.Races.OnFrame();
            _settling.OnFrame();
            _gap.OnFrame();
            if (_queue.Drain(_generation) > 0)
                _changed = true;
            if (FirstStageTooLong())
                GoSlotAtATime();
        }
        catch (Exception e) when (e is not OutOfMemoryException && IsActive)
        {
            Failed(e);
            _changed = true;
        }
        if (!_changed)
            return;
        _changed = false;
        Changed?.Invoke();
    }

    /// <summary>Cancelled with the search: its plan stops too.</summary>
    public CancellationToken Cancelled => _cancel.Token;

    /// <summary>
    /// Cancels the search: it ends for the player at once, the stage in flight stops at its next check, and nothing
    /// it still sends is applied.
    /// </summary>
    public void Cancel()
    {
        MainThread.Assert("SearchRun.Cancel");
        if (!IsActive)
            return;
        _cancel.Cancel();
        _generation = _nextGeneration();
        _queue.Clear();
        End(Stopped());
    }

    /// <summary>Ends a search still active without an answer and frees what it holds.</summary>
    public void Dispose()
    {
        if (IsActive)
        {
            _cancel.Cancel();
            _generation = _nextGeneration();
            _fighter.Dispose();
        }
        _stageCancel?.Dispose();
        _cancel.Dispose();
    }

    // A round races some sets through every stage. The whole search is one round over every set; a search that
    // improves a slot at a time is a round per step. The held set is the one the others must beat: current gear, or
    // the best found so far. It is never dropped, so each round ends with its full result.
    private void StartRound(List<int> sets, int held)
    {
        _in = sets;
        _trials = Answers.Stages(Request.Trials, sets.Count);
        _roundAll = [.. sets];
        _held = held;
        _roundStage = 0;
        _progress.RoundStarts();
        _roundSets = sets.Count;
        _roundClock.Restart();
        if (!StartChecked())
            NextStage();
    }

    private void NextStage()
    {
        _roundStage++;
        Stage++;
        _clock.Restart();
        var trials = _trials[_roundStage - 1];
        // A set a round has fought for these trials already isn't fought again: its result is the same.
        var toFight = _in.Where(i => !_fought.ContainsKey((i, trials))).ToList();
        _fighting = toFight.Count;
        _stageCancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        _changed = true;
        if (toFight.Count == 0)
        {
            StageDone(null);
            return;
        }
        // Stages and runs number themselves from one counter, so the host never sees a number twice.
        var generation = _generation = _nextGeneration();
        var job = new SearchJob(
            _snapshot,
            _loadoutSlot,
            Request.Setup.Changes,
            _target,
            new DriveOptions(trials, Request.Limits ?? new TrialLimits(), Request.Setup.IgnoreRequirements),
            Seed,
            Request.Copies,
            [.. toFight.Select(i => _candidates[i])]
        );
        _fighter.Stage(
            job,
            new SearchReport(_queue, generation, (i, r, l, s) => CandidateDone(trials, i, r, l, s), StageDone, Failed),
            generation,
            _stageCancel.Token
        );
    }

    private void CandidateDone(int trials, int index, TargetResult result, Note? left, string? mainSkill)
    {
        result.Name = TargetName;
        Measured(index, mainSkill);
        var set = new StageResult(index, result, MetricOf(index));
        _fought[(index, trials)] = set;
        _last[index] = set;
        if (left != null)
            _leftOut.Add(new LeftOut(_candidates[index].Changes.ShortForm, left));
    }

    private void StageDone(PoolStats? stats)
    {
        LastStats = stats ?? LastStats;
        var trials = _trials[_roundStage - 1];
        Remeasure(trials);
        var fought = _in.Count;
        _progress.StageDone(fought, trials);
        var results = _in.Where(i => _fought.ContainsKey((i, trials))).Select(i => _fought[(i, trials)]).ToList();
        Weigh(results);
        var last = _roundStage == _trials.Count;
        if (_drops != null)
        {
            DropStageDone(trials, fought, results, last);
            return;
        }
        if (!last && !OwnStyle(results))
        {
            var kept = new List<int>();
            foreach (var group in Groups(results))
            {
                var step = Race.After([.. group.Select(Held)], Request.Goal, Margin, _prices);
                _steps.Add(step);
                kept.AddRange(step.In.Select(Unheld));
            }
            _in = [.. _in.Where(kept.Contains)];
        }
        if (_climb == null)
        {
            Summarise(trials, fought, _in.Count, _clock.Elapsed.TotalSeconds);
            Finished(results);
        }
        // A climb's steps time the fights as well as a whole search's stages: the next search's estimate needs one.
        if (stats != null)
            Timed(_fighting, trials, whole: _climb == null);
        if (!last)
        {
            NextStage();
            return;
        }
        if (_climb == null)
        {
            if (FightLeadersAgain(results))
                return;
            if (_passes != null && _plan != null && _catalogue != null)
            {
                StartClimbs(_plan, _catalogue, _passes.After(results, _candidates, FewestDeaths(), _catalogue, _plan));
                return;
            }
            var answer = NewAnswer(AnswerState.Found);
            answer.Reran = _behindAt != null;
            answer.BehindAtStage = _behindAt;
            Answers.Fill(
                answer,
                _candidates,
                results,
                _last,
                Request.Goal,
                Margin,
                Request.AllStyles,
                _catalogue,
                _prices
            );
            End(answer);
            return;
        }
        StepEnded(results, trials);
    }

    /// <summary>The margin the search races and ties by.</summary>
    public double Margin { get; init; } = Preferences.DefaultTieShare;

    public PoolStats? LastStats { get; private set; }

    private void Failed(Exception error)
    {
        Log.Error("search failed", error);
        var answer = NewAnswer(AnswerState.Failed);
        answer.Reason = error is HostException host ? host.Reason : Note.Of("failed: {0}", error.Message);
        End(answer);
    }

    private Answer NewAnswer(AnswerState state) =>
        new()
        {
            Kind = Request.Kind,
            Key = Key,
            Target = _target.Key,
            TargetName = TargetName,
            State = state,
            Metric = Request.Goal.Metric,
            Margin = Margin,
            Possible = (double)(_plan?.Possible ?? _candidates.Count),
            RuledOut = _plan?.RuledOut.ToDictionary(r => r.Key, r => (double)r.Value) ?? [],
            Simulated = _candidates.Count,
            LeftOut = [.. _leftOut],
            Stages = [.. _stages],
            Climbed = _climb != null,
            SkippedStyles = [.. _plan?.SkippedStyles ?? []],
            Seed = Seed,
            FoundAt = DateTime.UtcNow,
            Loadout = Request.Setup.Name,
        };

    private void End(Answer answer)
    {
        if (_settling.Start(answer, this, _catalogue, End) || _gap.Start(answer, this, _catalogue, _prices, End))
            return;
        var (sets, results) = _passes?.Races.Answered ?? (_candidates, _last);
        _passes?.Finish(answer, sets, results, _catalogue, _prices);
        WantsOwnSet(answer);
        Answer = answer;
        _fighter.Dispose();
        _changed = true;
        Log.Info($"search of {_target.Key} ended: {answer.State}, {_stages.Count} stages, {_candidates.Count} sets");
        Recorded(answer);
        Ended?.Invoke(answer);
    }
}
