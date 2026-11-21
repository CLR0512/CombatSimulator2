namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// Puts an answer together from what a search fought: the sets' last results and how the race ended. It adds no
/// number of its own: gains and differences are the race's, from the results.
/// </summary>
public static class Answers
{
    /// <summary>The source of an item the game's data gives none for.</summary>
    public static Note OtherSource { get; } = Note.Of("Other");

    /// <summary>A round of this many sets or more starts with a stage of <see cref="FirstRung"/> trials.</summary>
    public const int ManySets = 100;

    /// <summary>
    /// The trials of a big round's first stage: enough for the race's 95% ranges (a batch a trial) to drop the sets
    /// clearly behind at a quarter of the usual first stage's cost.
    /// </summary>
    public const int FirstRung = 25;

    /// <summary>The trial counts of a search's stages: 100, 400, then the setup's own, each only when larger than
    /// the one before and no larger than the last.</summary>
    public static IReadOnlyList<int> Stages(int trials) => Stages(trials, 0);

    /// <summary>
    /// The stages of a round of <paramref name="sets"/> sets: as <see cref="Stages(int)"/>, after a first stage of
    /// <see cref="FirstRung"/> trials when there are <see cref="ManySets"/> or more.
    /// </summary>
    public static IReadOnlyList<int> Stages(int trials, int sets)
    {
        var stages = new List<int>();
        int[] counts = sets >= ManySets ? [FirstRung, 100, 400, trials] : [100, 400, trials];
        foreach (var count in counts)
            if (count <= trials && (stages.Count == 0 || count > stages[^1]))
                stages.Add(count);
        return stages;
    }

    /// <summary>
    /// The goal's metric as one set fought it: <c>xp:main</c> is the skill the set trains, and kills when it
    /// trains none.
    /// </summary>
    public static string MetricFor(Goal goal, string? mainSkill)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (goal.Metric != Metrics.MainXp)
            return goal.Metric;
        return string.IsNullOrEmpty(mainSkill) ? Metrics.Kills : "xp:" + mainSkill;
    }

    /// <summary>
    /// The goal's metric for a set of <paramref name="style"/> that trains <paramref name="skill"/>, in a search whose
    /// current gear is of <paramref name="currentStyle"/> and trains <paramref name="currentSkill"/>. A set of current
    /// gear's style is measured in the skill current gear trains, as a comparison of the two would: a stance that
    /// trains another skill is no gain to the skill being trained. A set of another style can't train that skill and
    /// is measured in its own.
    /// </summary>
    public static string MetricFor(
        Goal goal,
        string? skill,
        string? style,
        string? currentSkill,
        string? currentStyle
    ) => MetricFor(goal, !string.IsNullOrEmpty(currentSkill) && style == currentStyle ? currentSkill : skill);

    /// <summary>
    /// A set as an answer shows it: per slot that differs from current gear the old and new item, its stance when
    /// the set names one, its result, and its gain over <paramref name="current"/>.
    /// </summary>
    public static SetResult Set(
        Candidate candidate,
        StageResult result,
        StageResult? current,
        GearCatalogue? catalogue,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(result);
        var set = new SetResult
        {
            Changes = candidate.Changes,
            Style = candidate.Style,
            Result = result.Result,
            Metric = result.Metric,
            Same = [.. candidate.Same],
        };
        foreach (var change in candidate.Changes)
        {
            if (change.Kind == ChangeKind.Stance)
                set.Stance = change.Value;
            if (change.Kind != ChangeKind.Equip)
                continue;
            var old = catalogue?.Current.GetValueOrDefault(change.Subject!);
            set.Slots.Add(
                new SlotChange(
                    change.Subject!,
                    Ids.Brief(change.Subject!),
                    old,
                    Name(catalogue, old),
                    change.Item,
                    Name(catalogue, change.Item)
                )
                {
                    Mark =
                        change.Item != null
                        && catalogue?.Item(change.Item) is { } item
                        && Mark.OfItem(item) is { Kind: not MarkKind.None } mark
                            ? mark
                            : null,
                }
            );
            if (change.Item != null && catalogue?.Item(change.Item) is { ConsumedOn.Count: > 0 } used)
                set.Assumes.Add(used.Name);
        }
        if (current != null && !candidate.IsCurrent)
            (set.Gain, set.Percent) = Race.Difference(current, result, prices);
        return set;
    }

    /// <summary>
    /// Fills <paramref name="answer"/> from the race's end over <paramref name="results"/>, the last result of
    /// each set still in. With several styles, each style's own best and ties are found among its sets.
    /// </summary>
    public static void Fill(
        Answer answer,
        IReadOnlyList<Candidate> candidates,
        IReadOnlyList<StageResult> results,
        IReadOnlyDictionary<int, StageResult> all,
        Goal goal,
        double share,
        bool byStyle,
        GearCatalogue? catalogue,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(all);
        var byIndex = candidates.ToDictionary(c => c.Index);
        var current = all.GetValueOrDefault(0);
        SetResult Shown(int index) => Set(byIndex[index], all[index], current, catalogue, prices);
        var end = Race.End(results, goal, share, prices);
        answer.State = end.State;
        answer.Margin = share;
        answer.Metric = current?.Metric ?? goal.Metric;
        answer.Current = current == null ? null : Shown(0);
        answer.Ties =
        [
            .. end.Ties.Select(tie =>
            {
                var set = Shown(tie.Index);
                set.FromBest = tie.FromBest;
                return set;
            }),
        ];
        switch (end.State)
        {
            case AnswerState.Found:
                answer.Best = Shown(end.Best!.Value);
                break;
            case AnswerState.NoneWithinLimit:
                // The closest may have been dropped at an earlier stage: its last result is the one it left with.
                var closest = DeathLimits.Closest(
                    all.Values.Select(r => (r.Index, r.Result, Race.Value(r, prices))).Where(r => r.Item3 != null),
                    Ranking.LowerIsBetter(goal.Metric)
                );
                answer.Closest = closest is { } index ? Shown(index) : null;
                break;
            case AnswerState.NoneKills:
                answer.Reason =
                    current == null ? null : Ranking.ReasonOf(current.Result, current.Result.State).FirstOrDefault();
                break;
        }
        if (!byStyle)
            return;
        foreach (var style in results.GroupBy(r => byIndex[r.Index].Style).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var styleEnd = Race.End([.. style], goal, share, prices);
            answer.ByStyle.Add(
                new StyleAnswer
                {
                    Style = style.Key,
                    Best = styleEnd.Best is { } best ? Shown(best) : null,
                    Ties =
                    [
                        .. styleEnd.Ties.Select(tie =>
                        {
                            var set = Shown(tie.Index);
                            set.FromBest = tie.FromBest;
                            return set;
                        }),
                    ],
                }
            );
        }
    }

    /// <summary>
    /// Fills a Droptimizer answer from the candidates' last results: each swap that is a gain over current gear,
    /// largest first, with what it replaces and where the item comes from; the rest are counted as no gain.
    /// </summary>
    public static void FillDrops(
        Answer answer,
        DropPlan plan,
        IReadOnlyList<StageResult> results,
        Goal goal,
        double share,
        GearCatalogue catalogue,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(catalogue);
        var byIndex = plan.Candidates.ToDictionary(c => c.Index);
        var current = results.FirstOrDefault(r => r.Index == 0);
        var lower = Ranking.LowerIsBetter(goal.Metric);
        answer.Margin = share;
        answer.Metric = current?.Metric ?? goal.Metric;
        answer.Current = current == null ? null : Set(byIndex[0], current, null, catalogue, prices);
        answer.Unowned = plan.Unowned;
        answer.Wearable = plan.Wearable;
        answer.NotYetWearable = [.. plan.NotYetWearable];
        foreach (var set in results.Where(r => r.Index != 0))
        {
            var isGain = current != null && Droptimizer.IsGain(current, set, goal, share, prices);
            var isSafer = !isGain && current != null && Droptimizer.IsSafer(current, set, goal, share, prices);
            if (!isGain && !isSafer)
            {
                if (Race.Value(set, prices) != null && !DeathLimits.Within(goal.Deaths, set.Result))
                    answer.OverLimit++;
                else
                    answer.NoGain++;
                continue;
            }
            var (item, slot) = plan.Items[set.Index];
            var gear = catalogue.Item(item);
            var (gain, percent) = Race.Difference(current!, set, prices);
            // What the swap takes off: what is in its slot, and what is in the slots it takes beside it.
            var displaced = (gear?.Occupies ?? [])
                .Prepend(slot)
                .Select(s => catalogue.Current.GetValueOrDefault(s))
                .OfType<string>()
                .Select(id => Name(catalogue, id)!)
                .ToList();
            (isGain ? answer.Gains : answer.Safer).Add(
                new DropGain
                {
                    Item = item,
                    Name = Name(catalogue, item)!,
                    Slot = slot,
                    SlotName = Ids.Brief(slot),
                    Displaced = displaced,
                    Source = gear?.Source ?? OtherSource,
                    Changes = byIndex[set.Index].Changes,
                    Result = set.Result,
                    Metric = set.Metric,
                    Gain = gain,
                    Percent = percent,
                    TakenBefore = isSafer ? FightNumbers.Estimate(current!.Result, FightNumbers.Taken) : null,
                    Taken = isSafer ? FightNumbers.Estimate(set.Result, FightNumbers.Taken) : null,
                }
            );
        }
        answer.Safer = [.. answer.Safer.OrderBy(g => g.Taken?.Point ?? double.MaxValue)];
        // By the gain; against a current gear with no value, by the swap's own value.
        double Rank(DropGain g) =>
            g.Gain?.Point ?? Metrics.Estimate(g.Result, g.Metric, TimeUnit.Hour, prices)?.Point ?? 0;
        answer.Gains = [.. answer.Gains.OrderByDescending(g => lower ? -Rank(g) : Rank(g))];
        answer.State = answer.Gains.Count > 0 ? AnswerState.Found : AnswerState.KeepCurrent;
    }

    /// <summary>The fight numbers an answer explains a set by: chance to hit, damage per hit, damage taken a second.</summary>
    public static IReadOnlyList<string> WhyKeys { get; } = [FightNumbers.Hit, FightNumbers.PerHit, FightNumbers.Taken];

    /// <summary>
    /// Why a set does what it does beside current gear: each of <see cref="WhyKeys"/> for both, from what the game
    /// reported in their fights. A side whose result holds no counts, or nothing to measure, has null there.
    /// </summary>
    public static IReadOnlyList<(string Key, Estimate? Current, Estimate? Set)> Why(SetResult current, SetResult set)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(set);
        return
        [
            .. WhyKeys.Select(key =>
                (key, FightNumbers.Estimate(current.Result, key), FightNumbers.Estimate(set.Result, key))
            ),
        ];
    }

    private static string? Name(GearCatalogue? catalogue, string? item) =>
        item == null ? null : catalogue?.Item(item)?.Name ?? Ids.Brief(item);
}
