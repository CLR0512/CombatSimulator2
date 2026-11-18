using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// A climb's step and Droptimizer race their sets fought once (RaceChecks): the held set, then the rest read at the
// stages' counts. Once all are back, the round ends as a staged round's last stage does.
internal sealed partial class SearchRun
{
    // Starts the round fought once when it races that way; false when it keeps its stages.
    private bool StartChecked()
    {
        if (!RaceChecks.Apply(_target.Kind, _climb != null || _drops != null, Request.AllStyles, _trials.Count))
            return false;
        var race = new RaceChecks(_trials, _held);
        _roundStage = _trials.Count;
        Stage++;
        _clock.Restart();
        var counts = race.At.Append(race.Trials);
        if (counts.All(c => _fought.ContainsKey((_held, c))))
            FightOthers(race, _in.Count);
        else
            FightChecked(race, [_held], race.Reading(Request.Goal, Margin), _ => FightOthers(race, _in.Count));
        return true;
    }

    private void FightOthers(RaceChecks race, int sets)
    {
        var heldAt = race.At.TakeWhile(c => _fought.ContainsKey((_held, c))).Select(c => _fought[(_held, c)].Result);
        var rule = race.Rule([.. heldAt], MetricOf(_held), Request.Goal, Margin, _drops != null, _current, _prices);
        var others = _in.Where(i => i != _held && !_fought.ContainsKey((i, race.Trials))).ToList();
        if (others.Count == 0)
            CheckedDone(race, sets, null);
        else
            FightChecked(race, others, rule, stats => CheckedDone(race, sets, stats));
    }

    private void FightChecked(RaceChecks race, List<int> sets, CheckRule rule, Action<PoolStats?> done)
    {
        _stageCancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        _changed = true;
        var generation = _generation = _nextGeneration();
        var job = new SearchJob(
            _snapshot,
            _loadoutSlot,
            Request.Setup.Changes,
            _target,
            new DriveOptions(race.Trials, Request.Limits ?? new TrialLimits(), Request.Setup.IgnoreRequirements),
            Seed,
            Request.Copies,
            [.. sets.Select(i => _candidates[i])],
            Checks: rule
        );
        _fighter.Stage(
            job,
            new SearchReport(_queue, generation, (i, r, l, s) => CheckedCandidate(race, i, r, l, s), done, Failed),
            generation,
            _stageCancel.Token
        );
    }

    // A set's fight is back: its results at each count it reached are kept as that stage's would be, and a set
    // stopped at a check leaves the round.
    private void CheckedCandidate(RaceChecks race, int index, TargetResult fought, Note? left, string? mainSkill)
    {
        Measured(index, mainSkill);
        var metric = MetricOf(index);
        var (read, _, stays) = race.Took(index, fought, metric);
        foreach (var (count, at) in read)
        {
            at.Name = TargetName;
            _fought[(index, count)] = new StageResult(index, at, metric);
        }
        _last[index] = _fought[(index, read[^1].Count)];
        if (!stays && index != _held)
            _in.Remove(index);
        if (left != null)
            _leftOut.Add(new LeftOut(_candidates[index].Changes.ShortForm, left));
    }

    // Every set is back: each check is summarised as its stage, the time is noted for the next search's estimate,
    // and the round ends as its last stage would.
    private void CheckedDone(RaceChecks race, int sets, PoolStats? stats)
    {
        if (_drops != null)
        {
            foreach (var (trials, fought, left) in race.Summaries(sets))
                Summarise(trials, fought, left, _clock.Elapsed.TotalSeconds);
            foreach (var why in race.Reasons)
            {
                if (why == DropReason.OverLimit)
                    _overLimit++;
                else if (why != DropReason.Refused)
                    _noGain++;
            }
        }
        SearchTimes.Record(_target.Key, _target.Kind, 1, (int)race.Fought, _clock.Elapsed.TotalSeconds, whole: false);
        _fighting = 0;
        StageDone(stats);
    }
}
