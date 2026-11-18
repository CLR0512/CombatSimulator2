namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// An item Droptimizer tries and the slot it goes in: a record, not a tuple, as the plan crosses from the simulator
/// host as JSON, which keeps a record's properties and drops a tuple's fields.
/// </summary>
public sealed record DropItem(string Item, string Slot);

/// <summary>
/// What Droptimizer will fight: current gear, then one candidate per unowned item the character could wear now,
/// each swapped alone into current gear. <see cref="Items"/> names each candidate's item and the slot it goes in.
/// Unowned items that can't be worn yet are listed with what they are missing, and never fought.
/// </summary>
public sealed record DropPlan
{
    public IReadOnlyList<Candidate> Candidates { get; init; } = [];

    /// <summary>Each candidate's item and the slot it goes in, by candidate index.</summary>
    public IReadOnlyDictionary<int, DropItem> Items { get; init; } = new Dictionary<int, DropItem>();
    public IReadOnlyList<NotYetWearable> NotYetWearable { get; init; } = [];

    /// <summary>Unowned items read, wearable or not.</summary>
    public int Unowned { get; init; }

    /// <summary>Those wearable now, before any is ruled out.</summary>
    public int Wearable { get; init; }

    /// <summary>Wearable items another unowned item of their slot beats.</summary>
    public int RuledOut { get; init; }
}

/// <summary>How a stage of Droptimizer left its candidates, each judged against current gear alone.</summary>
public sealed record DropStep(IReadOnlyList<int> In, IReadOnlyDictionary<int, DropReason> Dropped);

/// <summary>Droptimizer's rules: its candidates, and which of them are gains over current gear.</summary>
public static class Droptimizer
{
    /// <summary>
    /// One candidate per unowned, wearable, not excluded item: current gear with the item in the first slot it
    /// fits that is empty, else the first it fits. A weapon fights in the stance fought in now when its type has
    /// it, else in its type's first. With comparing, an item another unowned item of its slot beats, in everything
    /// counted for the stance fought in now, is left out.
    /// </summary>
    public static DropPlan Plan(GearCatalogue catalogue, SearchRequest request, bool? comparing = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(request);
        var unowned = catalogue.Items.Where(i => i.Owned == 0 && !request.Excluded.Contains(i.Id)).ToList();
        var wearable = unowned.Where(i => i.Wearable && Slot(catalogue, i) != null).ToList();
        var left = wearable;
        if (comparing ?? Goal.AllowsComparing(request.Goal.Metric))
        {
            var measure = new Measure(catalogue, request.Goal, catalogue.Stance);
            left =
            [
                .. wearable.Where(
                    (item, at) =>
                        item.Weapon != null
                        || !wearable
                            .Where(
                                (other, place) => place != at && other.SlotType == item.SlotType && other.Weapon == null
                            )
                            .Any(other =>
                                measure.Beats(other.Id, item.Id)
                                && (!measure.Beats(item.Id, other.Id) || wearable.IndexOf(other) < at)
                            )
                ),
            ];
        }
        var style = catalogue.Items.FirstOrDefault(i => i.Worn != null && i.Weapon != null)?.Weapon!.Style ?? "";
        var candidates = new List<Candidate> { new(0, ChangeList.Empty, style) };
        var items = new Dictionary<int, DropItem>();
        foreach (var item in left)
        {
            var slot = Slot(catalogue, item)!;
            var changes = new List<Change> { new(ChangeKind.Equip, slot, item.Id) };
            if (item.Weapon is { } weapon)
                changes.Add(
                    new Change(
                        ChangeKind.Stance,
                        null,
                        weapon.Stances.Contains(catalogue.Stance) ? catalogue.Stance : weapon.Stances[0]
                    )
                );
            items[candidates.Count] = new DropItem(item.Id, slot);
            candidates.Add(new Candidate(candidates.Count, ChangeList.Of(changes), item.Weapon?.Style ?? style));
        }
        return new DropPlan
        {
            Candidates = candidates,
            Items = items,
            NotYetWearable =
            [
                .. unowned
                    .Where(i => !i.Wearable)
                    .Select(i => new NotYetWearable(i.Id, i.Name, i.Source ?? Answers.OtherSource, [.. i.Unmet])),
            ],
            Unowned = unowned.Count,
            Wearable = wearable.Count,
            RuledOut = wearable.Count - left.Count,
        };
    }

    /// <summary>
    /// After a stage: a candidate goes on unless it has no result to compare, is over the death limit, or can't be
    /// a gain any more: the better end of the 95% range of its difference from current gear is within
    /// <paramref name="share"/> of current gear's value. Without a value for current gear there is nothing to
    /// measure a gain against, and every candidate inside the limit goes on.
    /// </summary>
    public static DropStep After(IReadOnlyList<StageResult> results, Goal goal, double share, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var current = results.FirstOrDefault(r => r.Index == 0);
        var scale = current != null && Race.Value(current, prices) is { } value ? Math.Abs(value.Point) : (double?)null;
        var dropped = new Dictionary<int, DropReason>();
        foreach (var set in results.Where(r => r.Index != 0))
        {
            if (Race.Value(set, prices) == null)
                dropped[set.Index] =
                    set.Result.State == TargetState.Refused ? DropReason.Refused : DropReason.Unfinished;
            else if (DeathLimits.Over(goal.Deaths, set.Result))
                dropped[set.Index] = DropReason.OverLimit;
            else if (scale is { } of && Race.Difference(current!, set, prices).Delta is { } delta)
            {
                // One that can't be a gain any more still races when it is safer: it is shown among the safer.
                var bestCase = lower ? -delta.Low : delta.High;
                if (bestCase <= share * of && !IsSafer(current!, set, goal, share, prices))
                    dropped[set.Index] = DropReason.Behind;
            }
        }
        return new DropStep([.. results.Select(r => r.Index).Where(i => !dropped.ContainsKey(i))], dropped);
    }

    /// <summary>
    /// Whether a candidate's last result is a gain: inside the death limit, with the whole 95% range of its
    /// difference from current gear above <paramref name="share"/> of current gear's value. When current gear
    /// finishes no fight it has no value, and any swap that does is a gain, as the search finds a best set then.
    /// </summary>
    public static bool IsGain(StageResult current, StageResult set, Goal goal, double share, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (!DeathLimits.Within(goal.Deaths, set.Result))
            return false;
        if (Race.Value(current, prices) is not { } value)
            return Race.Value(set, prices) != null;
        if (Race.Difference(current, set, prices).Delta is not { } delta)
            return false;
        var worstCase = Ranking.LowerIsBetter(goal.Metric) ? -delta.High : delta.Low;
        return worstCase > share * Math.Abs(value.Point);
    }

    /// <summary>
    /// Whether a candidate is safer at no cost: inside the death limit, no worse than current gear on the goal by
    /// more than <paramref name="share"/>, and taking clearly less damage a second: less by more than the share, and
    /// its whole 95% range below what current gear takes. Armour that only spares damage ties on a goal like XP, and
    /// would otherwise never be shown.
    /// </summary>
    public static bool IsSafer(StageResult current, StageResult set, Goal goal, double share, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(goal);
        if (!DeathLimits.Within(goal.Deaths, set.Result))
            return false;
        if (
            FightNumbers.Estimate(current.Result, FightNumbers.Taken) is not { } now
            || FightNumbers.Estimate(set.Result, FightNumbers.Taken) is not { } then
            || then.Point >= now.Point * (1 - share)
            || then.High >= now.Point
        )
            return false;
        if (Race.Value(current, prices) is not { } value)
            return Race.Value(set, prices) != null;
        if (Race.Difference(current, set, prices).Delta is not { } delta)
            return false;
        var change = Ranking.LowerIsBetter(goal.Metric) ? -delta.Point : delta.Point;
        return change >= -share * Math.Abs(value.Point);
    }

    // The slot an item would go in: the first it fits that is unlocked and empty, else the first it fits.
    private static string? Slot(GearCatalogue catalogue, GearItem item)
    {
        var fits = catalogue.Slots.Where(s => s.Unlocked && s.Type == item.SlotType).ToList();
        return (fits.FirstOrDefault(s => !catalogue.Current.ContainsKey(s.Id)) ?? fits.FirstOrDefault())?.Id;
    }
}
