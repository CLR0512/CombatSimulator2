using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// Plans a search on a copy prepared with its setup: the catalogue read from the copy, then the plan, or
/// Droptimizer's. The simulator host plans this way, so the game builds no copy for it; the game plans the same
/// way on its check copy when the search fights in its own process.
/// </summary>
internal static class SearchPlanning
{
    /// <summary>
    /// What <paramref name="what"/> asks of <paramref name="request"/>, read from <paramref name="copy"/>.
    /// <paramref name="perTrial"/> is how long a trial of this kind of target took the last search, which decides
    /// whether every set can be fought within the long-search limit; null when nothing has been measured.
    /// <paramref name="token"/> stops a plan whose search was cancelled.
    /// </summary>
    public static PlanReply On(
        Copy copy,
        SearchRequest request,
        PlanWhat what,
        double? perTrial,
        CancellationToken token
    )
    {
        var catalogue = Game.Search.GearCatalog.Read(copy, TargetSpec.FromKey(request.Target));
        if (request.Kind != SearchKind.BestGear || request.Kinds.Count == 0)
            return Gear(copy, catalogue, request, what, perTrial, token);
        // A setup search: the gear's plan, with the choices read from the same copy beside it. With spells to try, a
        // magic weapon is no longer left out for want of one: each magic set fights with the strongest it can cast.
        var all = Game.Search.ChoiceCatalog.Read(copy);
        var spells = request.Kinds.Contains(ChoiceKind.Spell)
            ? all.FirstOrDefault(c =>
                c.Kind == ChoiceKind.Spell && c.Options.Any(o => o.IsCandidate(request.IncludeUnowned))
            )
            : null;
        if (spells != null)
            catalogue = catalogue with
            {
                UnusableStyles = catalogue
                    .UnusableStyles.Where(s => s.Key != Choices.Magic)
                    .ToDictionary(s => s.Key, s => s.Value),
            };
        var reply = Gear(copy, catalogue, request with { Kinds = [] }, what, perTrial, token);
        if (spells != null && reply.Plan is { } plan)
            reply = reply with
            {
                Plan = plan with
                {
                    Candidates = Choices.WithSpells(plan.Candidates, catalogue, spells, request.IncludeUnowned),
                },
            };
        return reply with { Choices = all, Supplies = Choices.SuppliesHash(all) };
    }

    private static PlanReply Gear(
        Copy copy,
        GearCatalogue catalogue,
        SearchRequest request,
        PlanWhat what,
        double? perTrial,
        CancellationToken token
    )
    {
        if (what == PlanWhat.Catalogue)
            return new PlanReply(catalogue);
        if (request.Check(catalogue) is { Count: > 0 } problems)
            return new PlanReply(catalogue) { Problem = problems[0] };
        if (request.Kind == SearchKind.Droptimizer)
            return new PlanReply(catalogue) { Drops = Droptimizer.Plan(catalogue, request) };
        return new PlanReply(catalogue)
        {
            Plan =
                what == PlanWhat.Counts
                    ? Planner.Plan(catalogue, request)
                    : Planned(copy, catalogue, request, perTrial, token),
        };
    }

    /// <summary>
    /// Whether fighting every set of <paramref name="plan"/> would pass the long-search limit, or can't be timed
    /// yet and is more than <see cref="PlanPreview.MostUntimed"/> sets.
    /// </summary>
    public static bool TooLong(SearchPlan plan, SearchRequest request, double? perTrial) =>
        !plan.Climb
        && (
            Expected(plan, request, perTrial) > SearchTimes.LongSearchSeconds
            || perTrial == null && plan.Candidates.Count > PlanPreview.MostUntimed
        );

    /// <summary>The seconds fighting every set of <paramref name="plan"/> is expected to take; null when not known.</summary>
    public static double? Expected(SearchPlan plan, SearchRequest request, double? perTrial) =>
        plan.Climb || perTrial is not { } seconds
            ? null
            : SearchTimes.Expected(seconds, plan.Candidates.Count, request.Trials);

    // Every set left to fight, those with equal stats against the target fought once, when the search is expected to
    // end within the long-search limit (or, before any is timed, when there are few); else a slot at a time.
    private static SearchPlan Planned(
        Copy copy,
        GearCatalogue catalogue,
        SearchRequest request,
        double? perTrial,
        CancellationToken token
    )
    {
        var plan = Planner.Plan(catalogue, request);
        if (plan.Climb)
            return plan;
        // Too many to fight untimed whatever merging would save: no stats are read for them.
        if (TooLong(plan, request, perTrial) && perTrial == null)
            return Planner.Plan(catalogue, request with { SlotAtATime = true });
        plan = Merged(copy, catalogue, plan, request, token);
        if (!TooLong(plan, request, perTrial))
            return plan;
        return Planner.Plan(catalogue, request with { SlotAtATime = true });
    }

    // Reads each candidate's stats against the target and merges those equal into one fight.
    private static SearchPlan Merged(
        Copy copy,
        GearCatalogue catalogue,
        SearchPlan plan,
        SearchRequest request,
        CancellationToken token
    )
    {
        if (plan.Candidates.Count < 3)
            return plan;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var keys = Game.Search.TargetStatsKey.Read(
            copy,
            TargetSpec.FromKey(request.Target),
            catalogue,
            plan.Candidates,
            request.Setup.IgnoreRequirements,
            token
        );
        if (keys == null)
        {
            Log.Info("search plan: no stats read against the target, nothing merged");
            return plan;
        }
        var keyed = plan.Candidates.Select((c, i) => c with { StatsKey = keys[i] }).ToList();
        var merged = StatsKeys.Merge(keyed, out var count);
        Log.Info(
            $"search plan: {count} of {plan.Candidates.Count} sets have the same stats as another, read in {watch.Elapsed.TotalSeconds:0.0} s"
        );
        var ruledOut = new Dictionary<string, System.Numerics.BigInteger>(plan.RuledOut)
        {
            [SearchPlan.SameStats] = count,
        };
        return plan with { Candidates = merged, RuledOut = ruledOut };
    }
}
