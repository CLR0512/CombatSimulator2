using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// A setup search's close calls: options of one slot or choice its steps couldn't tell apart, raced again once the
/// search is done, in the finished setup. The setup as it is, the finished setup and each variant with a runner-up
/// in place fight one stage together, four times the search's trials, on a seed of their own, so all three are
/// paired. A variant that clearly wins takes the finished setup's place, as does one still tied that holds the setup's
/// own option or one the character has more of; it is then fought once more at the search's own seed and trials, and
/// the answer read from that, so a run of the set opened gives the answer's figures. Each close call is named in the
/// answer with its difference.
/// </summary>
internal sealed class CloseCallRaces
{
    /// <summary>How many times the search's last stage a close call's race fights.</summary>
    public const int Longer = 4;

    private readonly ResultQueue _queue = new();
    private readonly Dictionary<string, (ChangeList Kept, ChangeList RunnerUp)> _noted = new(StringComparer.Ordinal);
    private readonly Dictionary<int, StageResult> _results = [];
    private List<Candidate> _sets = [];
    private SearchRun? _run;
    private Answer? _answer;
    private Action<Answer>? _end;
    private int _generation = -1;
    private ulong _seed;

    // The gear's names and what is worn, for an answer read again from this race.
    private GearCatalogue? _catalogue;

    // The finished setup's place among the race's sets: 0 when it is the setup as it is.
    private int _finished;

    /// <summary>Whether any close call was noted.</summary>
    public bool Any => _noted.Count > 0;

    /// <summary>
    /// The race's sets and results once a variant has taken the finished setup's place: the answer was read from
    /// them, so it is completed against them, not the search's own, which number other sets and fought fewer trials.
    /// Null while the answer stands as the steps found it.
    /// </summary>
    public (IReadOnlyList<Candidate> Sets, IReadOnlyDictionary<int, StageResult> Results)? Answered
    {
        get;
        private set;
    }

    /// <summary>
    /// Notes the ties of a step's race that differ from its winner in one slot or choice; a later step's close call
    /// on the same slot or choice takes the place of an earlier one.
    /// </summary>
    public void Note(ChangeList winner, IEnumerable<ChangeList> ties)
    {
        foreach (var tie in ties.Where(t => !t.Equals(winner) && CloseCalls.IsCloseCall(winner, t)))
            _noted[Subject(winner, tie)] = (winner, tie);
    }

    /// <summary>
    /// A set made after the plan (raced again, or settled) fights as the candidates it comes from did: gear the
    /// character can't wear yet, and any choice the search already tried, with their requirements ignored.
    /// </summary>
    public static ChangeList Ignoring(ChangeList set, SearchRun run, GearCatalogue? catalogue) =>
        ChangeList.Of(
            set.Where(c => catalogue?.Locked([c]).IsEmpty == false || run.Candidates.Any(k => k.Ignoring.Contains(c)))
        );

    /// <summary>
    /// Starts the race for the close calls that still apply to <paramref name="answer"/>'s finished setup, and
    /// calls <paramref name="end"/> with the answer once it is settled. False when there is none to race.
    /// </summary>
    public bool Start(Answer answer, SearchRun run, GearCatalogue? catalogue, Action<Answer> end)
    {
        if (answer.State is not (AnswerState.Found or AnswerState.KeepCurrent))
            return false;
        var finished = answer.Best?.Changes ?? ChangeList.Empty;
        var variants = _noted
            .Values.Select(n => Variant(finished, n.Kept, n.RunnerUp))
            .Where(v => v != null && !v.Equals(finished))
            .Distinct()
            .ToList();
        if (variants.Count == 0)
            return false;
        Answered = null;
        _finished = finished.IsEmpty ? 0 : 1;
        // The finished setup's style stays with it and its variants: settling and each style's best read it.
        var style = answer.Best?.Style ?? "";
        _sets = [new Candidate(0, ChangeList.Empty, "")];
        if (_finished == 1)
            _sets.Add(new Candidate(1, finished, style) { Ignoring = Ignoring(finished, run, catalogue) });
        foreach (var variant in variants)
            _sets.Add(new Candidate(_sets.Count, variant!, style) { Ignoring = Ignoring(variant!, run, catalogue) });
        (_run, _answer, _end, _catalogue) = (run, answer, end, catalogue);
        // A seed of its own, kept under 2^53 as the search's second seed is.
        _seed = (run.Seed * 2862933555777941757UL + 3037000493UL) >> 11;
        var request = run.Request;
        var trials = Math.Min(100_000, request.Trials * Longer);
        _generation = run.NewGeneration();
        var job = new SearchJob(
            run.Snapshot,
            run.LoadoutSlot,
            request.Setup.Changes,
            TargetSpec.FromKey(request.Target),
            new DriveOptions(trials, request.Limits ?? new TrialLimits(), request.Setup.IgnoreRequirements),
            _seed,
            request.Copies,
            _sets
        );
        Log.Info($"setup search: {variants.Count} close calls raced for {trials} trials with seed {_seed}");
        run.Fighter.Stage(
            job,
            new SearchReport(_queue, _generation, Done, _ => Settle(), Failed),
            _generation,
            run.Cancelled
        );
        return true;
    }

    /// <summary>Main thread: takes in what the race has sent.</summary>
    public void OnFrame()
    {
        if (_generation >= 0)
            _queue.Drain(_generation);
    }

    private void Done(int index, TargetResult result, Note? left, string? mainSkill)
    {
        var goal = _run!.Request.Goal;
        _results[index] = new StageResult(index, result, Answers.MetricFor(goal, mainSkill));
    }

    // The race failed: the answer stands as the steps found it, its close calls unsettled.
    private void Failed(Exception error)
    {
        Log.Warn("setup search: the close calls' race failed", error);
        Finish(_answer!);
    }

    private void Settle()
    {
        var run = _run!;
        var answer = _answer!;
        var lower = Ranking.LowerIsBetter(run.Request.Goal.Metric);
        var finished = _results.GetValueOrDefault(_finished);
        var held = _sets[_finished].Changes;
        if (finished == null)
        {
            Finish(answer);
            return;
        }
        var swapped = new List<(int Index, Estimate Value)>();
        var tied = new List<(int Index, Estimate Value)>();
        foreach (var variant in _sets.Skip(_finished + 1))
        {
            if (_results.GetValueOrDefault(variant.Index) is not { } fought)
                continue;
            if (Race.Difference(finished, fought) is not ({ } difference, { } percent))
                continue;
            var keys = DifferingKeys(held, variant.Changes);
            int Owned(ChangeList set) => CloseCalls.Owned(set, keys, id => _catalogue?.Item(id)?.Owned ?? 0);
            var (outcome, runnerUp) = CloseCalls.Settle(
                difference,
                lower,
                keys.TrueForAll(k => held.Find(k) == null),
                keys.TrueForAll(k => variant.Changes.Find(k) == null),
                Owned(held),
                Owned(variant.Changes)
            );
            var trials = run.Request.Trials * Longer;
            // A tie the runner-up takes is named the other way round: the option now held, beside the one it replaced.
            answer.CloseCalls.Add(
                outcome == CloseCallOutcome.Tie && runnerUp
                    ? Call(variant.Changes, held, Negated(percent), outcome, trials, _seed)
                    : Call(held, variant.Changes, percent, outcome, trials, _seed)
            );
            if (runnerUp && Race.Value(fought) is { } value)
                (outcome == CloseCallOutcome.Swapped ? swapped : tied).Add((variant.Index, value));
        }
        // A clear win comes before a tie the runner-up takes.
        if (swapped.Count == 0)
            swapped = tied;
        if (swapped.Count == 0)
        {
            Finish(answer);
            return;
        }
        // The variant that won by most takes the finished setup's place, fought again at the search's own seed and
        // trials beside current gear's result there: the race's own seed and length aren't what opening it runs.
        var best = lower ? swapped.MinBy(s => s.Value.Point).Index : swapped.MaxBy(s => s.Value.Point).Index;
        var won = _sets[best] with { Index = 1 };
        _sets = [new Candidate(0, ChangeList.Empty, ""), won];
        _results.Clear();
        if (answer.Current is { } now)
            _results[0] = new StageResult(0, now.Result, now.Metric);
        _generation = run.NewGeneration();
        var request = run.Request;
        run.Fighter.Stage(
            new SearchJob(
                run.Snapshot,
                run.LoadoutSlot,
                request.Setup.Changes,
                TargetSpec.FromKey(request.Target),
                new DriveOptions(request.Trials, request.Limits ?? new TrialLimits(), request.Setup.IgnoreRequirements),
                run.Seed,
                request.Copies,
                [won]
            ),
            new SearchReport(_queue, _generation, Done, _ => Confirmed(), Failed),
            _generation,
            run.Cancelled
        );
    }

    // The winner's result at the search's own seed: the answer is read from it and current gear's.
    private void Confirmed()
    {
        var answer = _answer!;
        if (_results.GetValueOrDefault(1) != null)
        {
            var kept = _results.Values.OrderBy(r => r.Index).ToList();
            Answers.Fill(answer, _sets, kept, _results, _run!.Request.Goal, _run.Margin, false, _catalogue);
            Answered = (_sets, new Dictionary<int, StageResult>(_results));
        }
        Finish(answer);
    }

    private void Finish(Answer answer)
    {
        _generation = -1;
        if (_run?.IsActive == true)
            _end!(answer);
    }

    // The close call with its sets; the answer names its options when it is filled.
    private static CloseCall Call(
        ChangeList kept,
        ChangeList variant,
        Estimate percent,
        CloseCallOutcome outcome,
        int trials,
        ulong seed
    )
    {
        return new CloseCall
        {
            Subject = Subject(kept, variant),
            KeptChanges = kept,
            RunnerUpChanges = variant,
            // As the answer reads it: the kept option's lead over the runner-up.
            Difference = percent,
            Trials = trials,
            Seed = seed,
            Outcome = outcome,
        };
    }

    // The finished setup with the runner-up's option for the slot or choice the close call was about; null when
    // the finished setup no longer holds the option it kept.
    private static ChangeList? Variant(ChangeList finished, ChangeList kept, ChangeList runnerUp)
    {
        var keys = DifferingKeys(kept, runnerUp);
        if (keys.Any(k => finished.Find(k)?.Value != kept.Find(k)?.Value))
            return null;
        var variant = finished;
        foreach (var key in keys)
            variant = runnerUp.Find(key) is { } change ? variant.With(change) : variant.Without(key);
        return variant;
    }

    private static Estimate Negated(Estimate e) => e with { Point = -e.Point, Low = -e.High, High = -e.Low };

    private static List<ChangeKey> DifferingKeys(ChangeList a, ChangeList b) =>
        [.. a.Select(c => c.Key).Union(b.Select(c => c.Key)).Where(k => a.Find(k)?.Value != b.Find(k)?.Value)];

    // The slot or choice a close call is about: the slot's id, or the kind for a choice without one.
    private static string Subject(ChangeList a, ChangeList b) =>
        DifferingKeys(a, b) is [var key, ..] && key.Kind != ChangeKind.Prayer
            ? Change.KindName(key.Kind) + (key.Subject is { } s ? ":" + Ids.Brief(s) : "")
            : "prayers";
}
