using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>
/// A master on its best setup: each monster on its picked loadout (<see cref="LoadoutPicks"/>), then the best blocks
/// within the slots open over those monsters (<see cref="BlockSuggestions"/>), worked out from Best loadouts' fights
/// with no more. <see cref="Loadouts"/> and <see cref="Blocks"/> are the changes that put it on a side (null when the
/// side's own is kept); <see cref="Figure"/> its figure in the goal's metric, <see cref="Gain"/> over every monster on
/// the setup with its blocks, paired by batch; <see cref="Stuck"/> the monster it gets stuck on, when it does.
/// </summary>
public sealed record BestMaster(
    string Master,
    Change? Loadouts,
    Change? Blocks,
    IReadOnlyList<string> Blocked,
    BlockChoice? Block,
    int Swaps,
    Estimate? Figure,
    Estimate? Gain,
    Estimate? Deaths,
    string? Stuck
);

/// <summary>Every master's best setup, best first. Godot-free.</summary>
public static class BestMasters
{
    /// <summary>
    /// <paramref name="master"/>'s best setup from its choices' results (the setup first, as <see cref="LoadoutPicks"/>
    /// takes them) and their picks; null when the setup's result has no monsters.
    /// </summary>
    public static BestMaster? For(
        string master,
        IReadOnlyList<(string? Choice, TargetResult Master)> results,
        LoadoutPicksAnswer? picks,
        Goal goal,
        TimeUnit unit = TimeUnit.Hour,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        if (results.Count == 0 || results[0].Master is not { Parts: { Count: > 0 } setupParts } setup || picks == null)
            return null;
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var picked = Picked(results, picks, setup.Blocked ?? [])!;
        var parts = picked.Parts!;
        var suggestion = BlockSuggestions.For(picked, goal.Metric, unit, prices, lower);
        var current = (setup.Blocked ?? []).Order(StringComparer.Ordinal).ToList();
        var block = suggestion is { AsGood: false, Best: { } best } ? best : null;
        var blocked = block?.Blocks.ToList() ?? current;
        var left = parts.Where(p => !blocked.Contains(Ids.PartTarget(p.Key))).ToList();
        // A blocked monster is never fought, so a loadout picked for it would only be a change that does nothing.
        var fought = picks with
        {
            Picks = [.. picks.Picks.Where(p => !blocked.Contains(Ids.PartTarget(p.Monster)))],
        };
        var figure = MasterFigures.Estimate(left, goal.Metric, unit, prices);
        var setupCounted = MasterParts.Counted(setup);
        var gain =
            figure != null
            && MasterFigures.Estimate(setupCounted, goal.Metric, unit, prices) is { } setupFigure
            && MasterFigures.Series(setupCounted, goal.Metric, unit, prices) is { } a
            && MasterFigures.Series(left, goal.Metric, unit, prices) is { } b
                ? Differences.Percent(
                    setupFigure,
                    figure,
                    Differences.Paired(a.Values, a.Denominators, b.Values, b.Denominators)
                )
                : null;
        var masterId = Ids.MasterOf(master);
        var blocks =
            block == null || block.Blocks.SequenceEqual(current)
                ? null
                : new Change(
                    ChangeKind.Blocks,
                    masterId,
                    block.Blocks.Count == 0
                        ? Change.None
                        : string.Join(',', block.Blocks.Select(k => k[(k.LastIndexOf('/') + 1)..]))
                );
        return new BestMaster(
            master,
            fought.Change,
            blocks,
            blocked,
            block,
            fought.Picks.Count(p => p.Choice != null),
            figure,
            gain,
            figure == null ? null : MasterFigures.Estimate(left, Metrics.Deaths, unit, prices),
            figure == null ? MasterFigures.Stuck(left)?.Key : null
        );
    }

    /// <summary>
    /// The master as one result from its choices' fights (the setup first): each monster on its picked loadout, the
    /// setup's when it keeps it, with <paramref name="blocked"/> as its blocks. Null without the setup's monsters.
    /// </summary>
    public static TargetResult? Picked(
        IReadOnlyList<(string? Choice, TargetResult Master)> results,
        LoadoutPicksAnswer picks,
        IReadOnlyList<string> blocked
    )
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(picks);
        if (results.Count == 0 || results[0].Master is not { Parts: { Count: > 0 } setupParts } setup)
            return null;
        var chosen = picks.Picks.ToDictionary(p => p.Monster, p => p.Choice, StringComparer.Ordinal);
        return new TargetResult
        {
            Key = setup.Key,
            Kind = TargetKind.Master,
            State = TargetState.Finished,
            Parts =
            [
                .. setupParts.Select(part =>
                    chosen.GetValueOrDefault(part.Key) is { } slot
                    && results.FirstOrDefault(r => r.Choice == slot).Master?.Parts?.Find(p => p.Key == part.Key)
                        is { } on
                        ? on
                        : part
                ),
            ],
            Blocked = [.. blocked],
            Master = setup.Master,
        };
    }

    /// <summary>The masters best first by figure in the goal's direction; the stuck ones last, in the order given.</summary>
    public static IReadOnlyList<BestMaster> Ranked(IEnumerable<BestMaster> masters, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(masters);
        ArgumentNullException.ThrowIfNull(goal);
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var list = masters.ToList();
        return
        [
            .. list.Where(m => m.Figure != null).OrderBy(m => lower ? m.Figure!.Point : -m.Figure!.Point),
            .. list.Where(m => m.Figure == null),
        ];
    }
}
