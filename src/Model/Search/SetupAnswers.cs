namespace MelvorMods.CombatSimulator2.Model.Search;

// A setup search's answer beyond gear: which set among the ties it answers with, the sets beside it, and each set's
// choices, supplies and damage taken.
public static class SetupAnswers
{
    /// <summary>
    /// Completes a setup search's <paramref name="answer"/>, filled as a gear search's is: among every set as good
    /// as the best, the setup as it is, else the highest gain; then Cheaper and Safer from every set fought as long as the best; then, for every set shown, its choices
    /// against the setup's own (with a mark where the character lacks one), supplies and damage taken.
    /// <paramref name="choices"/> are every kind's, so eaten food is told apart from other supplies;
    /// <paramref name="tried"/> are those the search tried, which get a line each.
    /// </summary>
    public static void FillChoices(
        Answer answer,
        IReadOnlyList<Choice> choices,
        IReadOnlyList<Choice> tried,
        IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<int, StageResult> all,
        GearCatalogue? catalogue,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(tried);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(all);
        var foods = choices
            .Where(c => c.Kind == ChoiceKind.Food)
            .SelectMany(c => c.Options.SelectMany(o => o.Ids))
            .ToHashSet(StringComparer.Ordinal);
        bool IsFood(string id) => foods.Contains(id);
        var byIndex = candidates.ToDictionary(c => c.Index);
        var current = all.GetValueOrDefault(0);
        SetResult Shown(int index) => Answers.Set(byIndex[index], all[index], current, catalogue, prices);
        int IndexOf(SetResult set) => candidates.FirstOrDefault(c => c.Changes.Equals(set.Changes))?.Index ?? -1;
        if (answer.State == AnswerState.Found && answer.Best != null)
            Untie(answer, IndexOf, Shown, all);
        if (answer.Best is { } best && IndexOf(best) is var bestIndex and >= 0)
            answer.Alternatives = Beside(best, bestIndex, all, IsFood, Shown);
        answer.WinsThroughGap =
        [
            .. answer.Best?.Result.NotSimulated?.Select(g => Note.Of("{0}: {1}", g.Name, g.What.English())) ?? [],
        ];
        NameCloseCalls(answer, catalogue, choices);
        foreach (var set in ShownSets(answer))
            Describe(set, tried, IsFood);
    }

    // Every set fought as long as the best whose gain lies in the best's 95% range is as good as it: the setup as it
    // is wins among them, else the highest gain. A climb keeps the set it holds on a tie, so a set as good found in
    // one of its steps would otherwise never be the answer. The rest are listed as good, measured from the one picked.
    private static void Untie(
        Answer answer,
        Func<SetResult, int> indexOf,
        Func<int, SetResult> shown,
        IReadOnlyDictionary<int, StageResult> all
    )
    {
        var best = answer.Best!;
        if (best.Gain is not { } range)
            return;
        var group = new List<SetResult> { best };
        group.AddRange(answer.Ties.Where(t => indexOf(t) != indexOf(best)));
        foreach (var (index, result) in all)
        {
            if (
                index == 0
                || result.Result.TrialsDone != best.Result.TrialsDone
                || group.Exists(s => indexOf(s) == index)
            )
                continue;
            // Other styles keep their own best, beside this one.
            var set = shown(index);
            if (set.Style == best.Style && set.Gain is { } gain && gain.Point >= range.Low && gain.Point <= range.High)
                group.Add(set);
        }
        // Sets whose choices change nothing in these fights fought the same fights: each such result counts once.
        group = [.. group.DistinctBy(s => ResultsHashInput.Brief([s.Result]))];
        var lower = Ranking.LowerIsBetter(best.Metric);
        // A set settling made, which the search never fought, stays: its slots were decided past the goal's ties.
        var picked =
            (indexOf(best) < 0 ? best : null)
            ?? group.Find(s => s.Changes.IsEmpty)
            ?? (
                lower
                    ? group.MinBy(s => s.Gain?.Point ?? double.MaxValue)
                    : group.MaxBy(s => s.Gain?.Point ?? double.MinValue)
            )!;
        var from = new StageResult(0, picked.Result, picked.Metric);
        answer.Ties =
        [
            .. group
                .Where(s => !ReferenceEquals(s, picked) && !s.Changes.IsEmpty)
                .OrderBy(s => lower ? s.Gain?.Point ?? double.MaxValue : -(s.Gain?.Point ?? double.MinValue))
                .Select(s =>
                {
                    // A set settling made wasn't one the search fought: it stands as it was shown.
                    var tie = indexOf(s) is var index and >= 0 ? shown(index) : s;
                    tie.FromBest = Race.Difference(from, new StageResult(1, s.Result, s.Metric)).Delta;
                    return tie;
                }),
        ];
        if (picked.Changes.IsEmpty)
        {
            answer.State = AnswerState.KeepCurrent;
            answer.Best = null;
            return;
        }
        answer.Best = picked;
        if (answer.ByStyle.Find(s => s.Style == picked.Style) is { } style)
            style.Best = picked;
    }

    // Cheaper and Safer among the sets fought as many trials as the best.
    private static Alternatives? Beside(
        SetResult best,
        int bestIndex,
        IReadOnlyDictionary<int, StageResult> all,
        Func<string, bool> isFood,
        Func<int, SetResult> shown
    )
    {
        AlternativePicks.Fought Fought(int index, SetResult set) =>
            new(index, set.Gain, SupplyCosts.Of(set.Result, isFood), SupplyCosts.DamageTaken(set.Result));
        var sets = all.Where(r =>
                r.Key != 0 && r.Key != bestIndex && r.Value.Result.TrialsDone == best.Result.TrialsDone
            )
            .Select(r => Fought(r.Key, shown(r.Key)))
            .ToList();
        var (cheaper, safer) = AlternativePicks.Pick(Fought(bestIndex, best), sets);
        if (cheaper == null && safer == null)
            return null;
        return new Alternatives
        {
            Cheaper = cheaper is { } c ? shown(c) : null,
            Safer = safer is { } s ? shown(s) : null,
        };
    }

    // Each close call's options by the game's names: an item, or a choice's option, the setup's own where the set
    // changes nothing there.
    private static void NameCloseCalls(Answer answer, GearCatalogue? catalogue, IReadOnlyList<Choice> choices)
    {
        foreach (var call in answer.CloseCalls)
        {
            var keys = call
                .KeptChanges.Select(c => c.Key)
                .Union(call.RunnerUpChanges.Select(c => c.Key))
                .Where(k => call.KeptChanges.Find(k)?.Value != call.RunnerUpChanges.Find(k)?.Value)
                .ToList();
            (call.KeptIds, call.Kept) = Options(call.KeptChanges, keys, catalogue, choices);
            (call.RunnerUpIds, call.RunnerUp) = Options(call.RunnerUpChanges, keys, catalogue, choices);
        }
    }

    // The options a set holds where a close call differs, as ids and as the game names them.
    private static (List<string> Ids, string Names) Options(
        ChangeList set,
        IReadOnlyList<ChangeKey> keys,
        GearCatalogue? catalogue,
        IReadOnlyList<Choice> choices
    )
    {
        var ids = new List<string>();
        var names = new List<string>();
        foreach (var key in keys)
        {
            var change = set.Find(key);
            if (key.Kind == ChangeKind.Equip)
            {
                var item = change == null ? catalogue?.Current.GetValueOrDefault(key.Subject!) : change.Item;
                if (item != null)
                {
                    ids.Add(item);
                    names.Add(catalogue?.Item(item)?.Name ?? Ids.Brief(item));
                }
                continue;
            }
            var choice = choices.FirstOrDefault(c =>
                ChoiceKinds.Change(c.Kind) == key.Kind && (c.Subject == null || c.Subject == key.Subject)
            );
            if (key.Kind == ChangeKind.Prayer)
            {
                if (change is { TurnsOn: true } || change == null && choice?.Current.Ids.Contains(key.Subject!) == true)
                {
                    ids.Add(key.Subject!);
                    names.Add(choice == null ? Ids.Brief(key.Subject!) : NameIn(choice, key.Subject!));
                }
                continue;
            }
            var id =
                change == null ? (choice?.Current.Ids is [var own, ..] ? own : null)
                : change.Value == Change.None ? null
                : change.Value;
            if (id != null)
            {
                ids.Add(id);
                names.Add(choice == null ? Ids.Brief(id) : NameIn(choice, id));
            }
        }
        return (ids, names.Count == 0 ? "none" : string.Join(", ", names));
    }

    /// <summary>Names every supply an answer shows, with the game's names for the ids in their keys.</summary>
    public static void NameSupplies(Answer answer, Func<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(names);
        List<SupplyCost> Named(List<SupplyCost> costs) =>
            [.. costs.Select(c => c with { Name = Metrics.Describe(c.Key, names).Subject })];
        foreach (var set in ShownSets(answer))
        {
            set.Supplies = Named(set.Supplies);
            foreach (var choice in set.Choices)
                choice.Supplies = Named(choice.Supplies);
        }
    }

    private static IEnumerable<SetResult> ShownSets(Answer answer)
    {
        SetResult?[] sets =
        [
            answer.Current,
            answer.Best,
            answer.Closest,
            answer.Alternatives?.Cheaper,
            answer.Alternatives?.Safer,
            .. answer.Ties,
        ];
        return sets.OfType<SetResult>();
    }

    /// <summary>
    /// A set's choices against the setup's own, one per choice switched on, and what it uses: supplies an hour,
    /// food eaten and damage taken.
    /// </summary>
    public static void Describe(SetResult set, IReadOnlyList<Choice> choices, Func<string, bool> isFood)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(choices);
        set.Supplies = [.. SupplyCosts.Of(set.Result, isFood)];
        set.Food = SupplyCosts.Food(set.Result);
        set.DamageTaken = SupplyCosts.DamageTaken(set.Result);
        set.Choices = [.. choices.Select(c => Chosen(c, set.Changes, set.Supplies))];
    }

    // What the set holds for one choice, read from its changes, with the supplies that choice brings.
    private static ChoiceChange Chosen(Choice choice, ChangeList changes, IReadOnlyList<SupplyCost> supplies)
    {
        var kind = ChoicesKind(choice.Kind);
        IReadOnlyList<string> now = choice.Current.Ids;
        if (choice.Kind == ChoiceKind.Prayers)
            now =
            [
                .. now.Where(p => changes.Find(new ChangeKey(kind, p)) is not { TurnsOn: false })
                    .Concat(changes.Where(c => c.Kind == kind && c.TurnsOn).Select(c => c.Subject!))
                    .Distinct(StringComparer.Ordinal),
            ];
        else if (changes.Find(new ChangeKey(kind, choice.Subject is { } slot ? Ids.Full(slot) : null)) is { } change)
            now = change.Value == Change.None ? [] : [change.Value];
        var option = choice.Options.FirstOrDefault(o => o.Ids.Order().SequenceEqual(now.Order()));
        var singles = now.Select(id => choice.Options.FirstOrDefault(o => o.Ids.Count == 1 && o.Ids[0] == id)).ToList();
        var mark =
            option != null
                ? Mark.Of(option)
                : singles.OfType<ChoiceOption>().Select(Mark.Of).FirstOrDefault(m => m.Kind != MarkKind.None)
                    ?? Mark.None;
        return new ChoiceChange
        {
            Kind = choice.Kind,
            Subject = choice.Subject,
            Name = choice.Name,
            Old = [.. choice.Current.Ids],
            OldNames = [.. choice.Current.Ids.Select(id => NameIn(choice, id))],
            New = [.. now],
            NewNames = [.. now.Select(id => NameIn(choice, id))],
            Mark = now.Order().SequenceEqual(choice.Current.Ids.Order()) ? Mark.None : mark,
            Supplies = [.. supplies.Where(s => Brings(choice, now, s.Key))],
        };
    }

    private static ChangeKind ChoicesKind(ChoiceKind kind) => ChoiceKinds.Change(kind);

    private static string NameIn(Choice choice, string id) =>
        choice.Options.FirstOrDefault(o => o.Ids.Count == 1 && o.Ids[0] == id) is { Names.Count: > 0 } named
            ? named.Names[0]
            : Ids.Brief(id);

    // Prayers spend prayer points, the spell and imbuement runes (the spell shows the runes when both are on), a
    // toolbelt item its charges.
    private static bool Brings(Choice choice, IReadOnlyList<string> now, string key) =>
        choice.Kind switch
        {
            ChoiceKind.Prayers => now.Count > 0 && key == SupplyCosts.Used + "currency:" + Metrics.PrayerPoints,
            ChoiceKind.Spell => now.Count > 0 && key.StartsWith(SupplyCosts.Used + "item:", StringComparison.Ordinal),
            ChoiceKind.Toolbelt or ChoiceKind.Potion => now.Any(id => key == SupplyCosts.Used + "charges:" + id),
            _ => false,
        };

    /// <summary>
    /// <paramref name="set"/> as it differs from <paramref name="best"/>: the slots and choices it holds other than the
    /// best's (old as the best's), its stance when it is another, and its own result.
    /// </summary>
    public static SetResult Differences(SetResult set, SetResult best)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(best);
        var slots = new List<SlotChange>();
        var bestBy = best.Slots.ToDictionary(s => s.Slot);
        var setBy = set.Slots.ToDictionary(s => s.Slot);
        foreach (var slot in bestBy.Keys.Union(setBy.Keys).Order(StringComparer.Ordinal))
        {
            var b = bestBy.GetValueOrDefault(slot);
            var a = setBy.GetValueOrDefault(slot);
            var (bestItem, bestName) = b != null ? (b.New, b.NewName) : (a!.Old, a.OldName);
            var (item, name) = a != null ? (a.New, a.NewName) : (b!.Old, b.OldName);
            if (bestItem != item)
                slots.Add(new SlotChange(slot, (a ?? b)!.SlotName, bestItem, bestName, item, name) { Mark = a?.Mark });
        }
        var choices = new List<ChoiceChange>();
        foreach (var choice in set.Choices)
            if (best.Choices.Find(c => c.Name == choice.Name) is { } held && !held.New.SequenceEqual(choice.New))
                choices.Add(
                    new ChoiceChange
                    {
                        Kind = choice.Kind,
                        Subject = choice.Subject,
                        Name = choice.Name,
                        Old = held.New,
                        OldNames = held.NewNames,
                        New = choice.New,
                        NewNames = choice.NewNames,
                        Mark = choice.Mark,
                        Supplies = choice.Supplies,
                    }
                );
        return new SetResult
        {
            Changes = set.Changes,
            Slots = slots,
            Choices = choices,
            Stance = set.Stance != best.Stance ? set.Stance : null,
            Result = set.Result,
            Metric = set.Metric,
            Gain = set.Gain,
            Percent = set.Percent,
            Assumes = set.Assumes,
        };
    }
}
