using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// A gear search's settling: once its answer and close calls are done, every slot whose items the goal couldn't tell
/// apart is decided by the defensive measures (<see cref="Settling"/>). The options are the ties of the last step
/// that held that slot's (or, after a race over every set, the sets as good that differ from the best in that slot
/// alone), each put in the finished set. Current gear, the finished set and each variant fight one stage together,
/// four times the search's trials, on a seed of their own, so all are paired. When a slot changes, the settled set
/// is fought once more at the search's own seed and trials, so the answer holds its result as a run of it would.
/// Every slot of the answer then names its item and what decided it. When no set stayed within the death limit, the
/// closest set is settled instead: each slot it changes but its weapon's is raced against what is worn there, deaths
/// deciding first and the goal next, so a slot that changes nothing keeps what is worn.
/// </summary>
internal sealed class SettlingRaces
{
    private readonly ResultQueue _queue = new();

    // Per slot, what the latest step that tied on it put there: the winner's change and its ties' that differ in that
    // slot alone, by value, null where a set keeps what is worn. Read before the catalogue is, so worn is resolved later.
    private Dictionary<string, List<string?>> _ties = new(StringComparer.Ordinal);

    // The ties by the style of the climb that noted them: with every style, each climbs on its own, and a slot's ties
    // from one style's climb don't belong in another style's set.
    private readonly Dictionary<string, Dictionary<string, List<string?>>> _byStyle = new(StringComparer.Ordinal);
    private readonly Dictionary<int, StageResult> _results = [];
    private List<(string Slot, string? Item, ChangeList Set)> _variants = [];
    private SearchRun? _run;
    private Answer? _answer;
    private GearCatalogue? _catalogue;
    private Action<Answer>? _end;
    private ChangeList _finished = ChangeList.Empty;
    private Dictionary<string, (DecidedBy By, string? Against, Estimate? Difference)> _decided = [];
    private int _generation = -1;
    private ulong _seed;
    private bool _started;

    // Settling the closest set of an answer with no set within the death limit, not its best.
    private bool _closest;

    /// <summary>Notes a step's ties that differ from its winner's set in one equipment slot alone.</summary>
    public void Noted(RaceEnd end, Func<int, int> unheld, IReadOnlyList<Candidate> candidates, int winner) =>
        Note(
            candidates[winner].Changes,
            end.Ties.Select(t => candidates[unheld(t.Index)].Changes),
            candidates[winner].Style
        );

    private void Note(ChangeList winner, IEnumerable<ChangeList> ties, string style)
    {
        if (!_byStyle.TryGetValue(style, out var noted))
            _byStyle[style] = noted = new(StringComparer.Ordinal);
        var bySlot = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        foreach (var tie in ties)
        {
            if (SlotOnly(winner, tie) is not { } slot)
                continue;
            if (!bySlot.TryGetValue(slot, out var items))
                bySlot[slot] = items = [ChangeIn(winner, slot)];
            items.Add(ChangeIn(tie, slot));
        }
        foreach (var (slot, items) in bySlot)
            noted[slot] = items;
    }

    /// <summary>
    /// Settles <paramref name="answer"/> and calls <paramref name="end"/> with it once done; false when it is
    /// settled at once (nothing tied, or not a gear search's answer) and the caller ends it.
    /// </summary>
    public bool Start(Answer answer, SearchRun run, GearCatalogue? catalogue, Action<Answer> end)
    {
        if (_started)
        {
            // An answer a stop made while the slots were being settled: it says they weren't.
            if (answer != _answer && run.Request.Kind == SearchKind.BestGear && answer.Decisions.Count == 0)
                answer.Settled = false;
            return false;
        }
        if (run.Request.Kind != SearchKind.BestGear || catalogue == null)
            return false;
        // A stopped search keeps what it had; a master's or a set's answer is scored over several targets, which
        // one settling race doesn't fight.
        if (answer.StoppedAtStage != null || run.Cancelled.IsCancellationRequested)
        {
            answer.Settled = _byStyle.Values.Any(t => t.Count > 0) ? false : null;
            return false;
        }
        if (TargetSpec.FromKey(run.Request.Target).Kind is not (TargetKind.Enemy or TargetKind.Dungeon))
            return false;
        _started = true;
        (_run, _answer, _catalogue, _end) = (run, answer, catalogue, end);
        _closest = answer is { State: AnswerState.NoneWithinLimit, Closest: not null };
        if (answer.State is not (AnswerState.Found or AnswerState.KeepCurrent) && !_closest)
            return false;
        _finished = (_closest ? answer.Closest : answer.Best)?.Changes ?? ChangeList.Empty;
        if (!answer.Climbed && answer.Best != null)
            Note(_finished, answer.Ties.Select(t => t.Changes), answer.Best.Style);
        // With no best (current gear kept), a search of one style has its ties under that style alone.
        var style = _closest ? null : answer.Best?.Style ?? (_byStyle.Count == 1 ? _byStyle.Keys.First() : null);
        _ties = style != null && _byStyle.TryGetValue(style, out var ties) ? ties : new(StringComparer.Ordinal);
        _variants = _closest ? Reverts(catalogue) : [.. Tied(catalogue)];
        if (_variants.Count == 0)
        {
            Decide(answer);
            return false;
        }
        var sets = new List<Candidate> { new(0, ChangeList.Empty, "") };
        // As the search fought them: what it fought with a requirement ignored is ignored here too.
        sets.Add(new Candidate(1, _finished, "") { Ignoring = CloseCallRaces.Ignoring(_finished, run, catalogue) });
        foreach (var (_, _, set) in _variants)
            sets.Add(new Candidate(sets.Count, set, "") { Ignoring = CloseCallRaces.Ignoring(set, run, catalogue) });
        // A seed of its own, apart from the close calls' and kept under 2^53 as the search's second seed is.
        _seed = (run.Seed * 6364136223846793005UL + 1442695040888963407UL) >> 11;
        var trials = Math.Min(100_000, run.Request.Trials * CloseCallRaces.Longer);
        Log.Info(
            $"search settling{(_closest ? " the closest set" : "")}: {_variants.Count} options in {_variants.Select(v => v.Slot).Distinct().Count()} slots, {trials} trials, seed {_seed}"
        );
        Fight(sets, trials, _seed, Settle);
        return true;
    }

    // Each tied slot's other options, each in the finished set; not the weapon's slots, whose item carries the style
    // and stance the rest of the set was climbed for.
    private IEnumerable<(string Slot, string? Item, ChangeList Set)> Tied(GearCatalogue catalogue) =>
        [
            .. catalogue
                .Slots.Where(s => _ties.ContainsKey(s.Id) && !WeaponTakes(catalogue).Contains(s.Id))
                .SelectMany(s =>
                    _ties[s.Id]
                        .Select(change =>
                            change == null ? Worn(s.Id)
                            : change == Change.None ? null
                            : change
                        )
                        .Distinct()
                        // The finished set holds its own item already; what is worn stays an option, so a slot the
                        // goal can't tell apart keeps it by the tie order.
                        .Where(item => item != ItemIn(_finished, s.Id))
                        .Select(item => (s.Id, item, _finished.With(Equip(s.Id, item))))
                ),
        ];

    // The slots the finished set's weapon takes, worn or new.
    private HashSet<string> WeaponTakes(GearCatalogue catalogue) =>
        catalogue
            .Slots.Select(s => ItemIn(_finished, s.Id))
            .Where(item => item != null && catalogue.Item(item)?.Weapon != null)
            .SelectMany(item => catalogue.Item(item!)!.Occupies)
            .ToHashSet(StringComparer.Ordinal);

    // Each slot the closest set changes, put back to what is worn, in the closest set: the weapon's and those it
    // takes stay, as its stance goes with it.
    private List<(string Slot, string? Item, ChangeList Set)> Reverts(GearCatalogue catalogue)
    {
        var taken = WeaponTakes(catalogue);
        return
        [
            .. catalogue
                .Slots.Where(s =>
                    s.Unlocked
                    && !taken.Contains(s.Id)
                    && ChangeIn(_finished, s.Id) is { } change
                    && ItemIn(_finished, s.Id) != Worn(s.Id)
                    && catalogue.Item(change)?.Weapon == null
                    && (Worn(s.Id) is not { } worn || catalogue.Item(worn)?.Weapon == null)
                )
                .Select(s => (s.Id, Worn(s.Id), _finished.With(Equip(s.Id, Worn(s.Id))))),
        ];
    }

    /// <summary>Main thread: takes in what the race has sent.</summary>
    public void OnFrame()
    {
        if (_generation >= 0)
            _queue.Drain(_generation);
    }

    private void Fight(List<Candidate> sets, int trials, ulong seed, Action then)
    {
        var run = _run!;
        var request = run.Request;
        _results.Clear();
        _generation = run.NewGeneration();
        var job = new SearchJob(
            run.Snapshot,
            run.LoadoutSlot,
            request.Setup.Changes,
            TargetSpec.FromKey(request.Target),
            new DriveOptions(trials, request.Limits ?? new TrialLimits(), request.Setup.IgnoreRequirements),
            seed,
            request.Copies,
            sets
        );
        run.Fighter.Stage(
            job,
            new SearchReport(_queue, _generation, Done, _ => then(), Failed),
            _generation,
            run.Cancelled
        );
    }

    private void Done(int index, TargetResult result, Note? left, string? mainSkill) =>
        _results[index] = new StageResult(index, result, Answers.MetricFor(_run!.Request.Goal, mainSkill));

    // Stopped or failed: the answer stands as the search found it, its slots not settled.
    private void Failed(Exception error)
    {
        Log.Warn("search settling stopped", error);
        Decide(_answer!);
        _answer!.Settled = false;
        Finish();
    }

    // Each tied slot decided from the race; when one changes, the settled set is fought at the search's own seed.
    private void Settle()
    {
        var settled = _finished;
        _decided = [];
        foreach (var slot in _catalogue!.Slots.Where(s => _variants.Any(v => v.Slot == s.Id)))
        {
            var options = new List<SettleOption>();
            if (_results.GetValueOrDefault(1) is { } held)
                options.Add(Option(slot.Id, ItemIn(_finished, slot.Id), held.Result) with { Held = true });
            for (var i = 0; i < _variants.Count; i++)
                if (_variants[i].Slot == slot.Id && _results.GetValueOrDefault(i + 2) is { } fought)
                    options.Add(Option(slot.Id, _variants[i].Item, fought.Result));
            if (options.Count < 2)
                continue;
            // A found set stays within the death limit: an option outside it, fought this long, isn't one to take.
            var limit = _run!.Request.Goal.Deaths;
            if (!_closest && options.Where(o => DeathLimits.Within(limit, o.Result)).ToList() is { Count: > 0 } within)
            {
                var outside = options.Except(within).FirstOrDefault();
                if (within.Count == 1 && outside != null)
                {
                    _decided[slot.Id] = (DecidedBy.Deaths, outside.Name ?? "nothing", null);
                    if (within[0].Item != ItemIn(_finished, slot.Id))
                        settled = Put(settled, slot.Id, within[0].Item);
                    continue;
                }
                options = within;
            }
            var (winner, by, against, difference) = Settling.Decide(
                options,
                _results.Values.First().Metric,
                deathsFirst: _closest
            );
            _decided[slot.Id] = (by, against == null ? null : against.Name ?? "nothing", difference);
            if (winner.Item != ItemIn(_finished, slot.Id))
                settled = Put(settled, slot.Id, winner.Item);
        }
        if (settled.Equals(_finished))
        {
            Decide(_answer!);
            Finish();
            return;
        }
        Log.Info($"search settling: {settled.ShortForm} fought at the search's seed");
        _finished = settled;
        Fight(
            [new Candidate(1, settled, "") { Ignoring = CloseCallRaces.Ignoring(settled, _run!, _catalogue) }],
            _run!.Request.Trials,
            _run.Seed,
            Confirmed
        );
    }

    // The settled set's result beside current gear's, both at the search's seed and trials: the answer is read again.
    private void Confirmed()
    {
        var answer = _answer!;
        if (_closest && _results.GetValueOrDefault(1) is { } closest && answer.Current is { } now)
        {
            // Still no set within the limit: the settled set is the closest, as fought at the search's seed.
            var style = answer.Closest!.Style;
            answer.Closest = Answers.Set(
                new Candidate(1, _finished, style),
                closest,
                new StageResult(0, now.Result, now.Metric),
                _catalogue
            );
        }
        else if (_results.GetValueOrDefault(1) is { } settled && answer.Current is { } current)
        {
            var sets = new List<Candidate>
            {
                new(0, ChangeList.Empty, ""),
                new(1, _finished, answer.Best?.Style ?? ""),
            };
            var kept = new List<StageResult> { new(0, current.Result, current.Metric), settled };
            var all = kept.ToDictionary(r => r.Index);
            // Each style's own best stands as the search found it: settling was about the answer's set alone.
            var (byStyle, skipped) = (answer.ByStyle, answer.SkippedStyles);
            var found = (answer.State, answer.Metric, answer.Current, answer.Best, answer.Ties, answer.Closest);
            Answers.Fill(answer, sets, kept, all, _run!.Request.Goal, _run.Margin, false, _catalogue);
            (answer.ByStyle, answer.SkippedStyles) = (byStyle, skipped);
            // Fought again, the settled set left the death limit or has no value: the answer is the set the search
            // found, unsettled.
            if (answer.State is AnswerState.NoneWithinLimit or AnswerState.NoneKills || Race.Value(settled) == null)
            {
                (answer.State, answer.Metric, answer.Current, answer.Best, answer.Ties, answer.Closest) = found;
                answer.Reason = null;
                _decided = [];
                Log.Info("search settling: the settled set left the death limit; the found set stands");
            }
            // The settled set ties current gear on the goal, as its slots were the goal's ties, and a defensive
            // measure chose them: it is the answer, not current gear.
            if (answer.State == AnswerState.KeepCurrent)
            {
                answer.Best = Answers.Set(sets[1], settled, kept[0], _catalogue);
                answer.Ties.RemoveAll(t => t.Changes.Equals(_finished));
                answer.State = AnswerState.Found;
            }
        }
        Decide(answer);
        Finish();
    }

    // Every unlocked slot of the answer's set, in the game's order, with what decided it.
    private void Decide(Answer answer)
    {
        var catalogue = _catalogue!;
        var set = (_closest ? answer.Closest : answer.Best)?.Changes ?? ChangeList.Empty;
        answer.Decisions =
        [
            .. catalogue
                .Slots.Where(s => s.Unlocked)
                .Select(s =>
                {
                    var item = ItemIn(set, s.Id);
                    var (by, against, difference) = _decided.GetValueOrDefault(
                        s.Id,
                        (_run!.Request.Locks.Contains(s.Id) ? DecidedBy.Locked : DecidedBy.Goal, null, null)
                    );
                    return new SlotDecision(
                        s.Id,
                        Ids.Brief(s.Id),
                        item,
                        item == null ? null : catalogue.Item(item)?.Name ?? Ids.Brief(item),
                        item == Worn(s.Id),
                        item != null && catalogue.Item(item) is { } gear ? Mark.OfItem(gear) : null,
                        by,
                        against,
                        difference
                    );
                }),
        ];
        answer.Settled = true;
    }

    private void Finish()
    {
        _generation = -1;
        if (_run?.IsActive == true)
            _end!(_answer!);
    }

    private SettleOption Option(string slot, string? item, TargetResult result)
    {
        var gear = item == null ? null : _catalogue!.Item(item);
        return new SettleOption(
            item,
            gear?.Name,
            item == Worn(slot),
            item == null || gear?.Owned > 0,
            gear?.Unmet.Count ?? 0,
            item == null ? int.MaxValue : Order(item),
            result
        );
    }

    private string? Worn(string slot) => _catalogue!.Current.GetValueOrDefault(slot);

    // An item's place in the game's order, as the catalogue reads them.
    private int Order(string item)
    {
        var items = _catalogue!.Items;
        for (var i = 0; i < items.Count; i++)
            if (items[i].Id == item)
                return i;
        return int.MaxValue;
    }

    // The item a set puts in a slot: its change's, else what is worn there.
    private string? ItemIn(ChangeList set, string slot) =>
        set.Find(new ChangeKey(ChangeKind.Equip, slot)) is { } change
            ? (change.Value == Change.None ? null : change.Value)
            : Worn(slot);

    // The set with the slot holding the item; what is worn comes back as no change, so the slot reads "keep" as any
    // slot the set leaves alone.
    private ChangeList Put(ChangeList set, string slot, string? item) =>
        item == Worn(slot) ? set.Without(new ChangeKey(ChangeKind.Equip, slot)) : set.With(Equip(slot, item));

    private static Change Equip(string slot, string? item) => new(ChangeKind.Equip, slot, item ?? Change.None);

    private static string? ChangeIn(ChangeList set, string slot) =>
        set.Find(new ChangeKey(ChangeKind.Equip, slot))?.Value;

    // The one equipment slot two sets differ in, or null when they differ in anything else too.
    private static string? SlotOnly(ChangeList a, ChangeList b)
    {
        var keys = a.Select(c => c.Key)
            .Union(b.Select(c => c.Key))
            .Where(k => a.Find(k)?.Value != b.Find(k)?.Value)
            .ToList();
        return keys is [{ Kind: ChangeKind.Equip, Subject: { } slot }] ? slot : null;
    }
}
