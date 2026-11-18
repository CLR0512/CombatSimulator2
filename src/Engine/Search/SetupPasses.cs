using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// A setup search's passes over gear and the choices beyond it: gear first, as a gear search would search it, with
/// every choice held as the setup has it; then each kind switched on in turn with the gear held; then the gear again
/// with the new choices held, if any changed; until a pass changes nothing, three passes, or the long-search limit.
/// Where every gear set is raced at once, the choices' steps follow as a climb from the race's winner; where gear is
/// climbed, the choices' steps are the climb's own.
/// </summary>
internal sealed class SetupPasses
{
    private readonly IReadOnlyList<Choice> _all;
    private readonly SearchRequest _request;
    private readonly double _share;
    private readonly PriceTable _prices;

    private SetupPasses(
        ChoiceSteps steps,
        IReadOnlyList<Choice> all,
        SearchRequest request,
        double share,
        PriceTable prices
    )
    {
        Steps = steps;
        _all = all;
        _request = request;
        _share = share;
        _prices = prices;
    }

    public ChoiceSteps Steps { get; }

    /// <summary>The close calls noted in the steps, raced again once the search is done.</summary>
    public CloseCallRaces Races { get; } = new();

    /// <summary>
    /// Notes a step's close calls: its ties, by their places in the round (<paramref name="unheld"/> turns them
    /// into the search's), that differ from <paramref name="winner"/>'s set in one slot or choice.
    /// </summary>
    public void Noted(RaceEnd end, Func<int, int> unheld, IReadOnlyList<Candidate> candidates, int winner) =>
        Races.Note(candidates[winner].Changes, end.Ties.Select(t => candidates[unheld(t.Index)].Changes));

    /// <summary>
    /// The passes for <paramref name="request"/> over <paramref name="choices"/> read from the game: only the kinds
    /// it switches on that offer something to try. Null for a gear search, or when no kind has anything.
    /// </summary>
    public static SetupPasses? For(
        SearchRequest request,
        IReadOnlyList<Choice> choices,
        double share,
        PriceTable prices
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(choices);
        if (request.Kind != SearchKind.BestGear || request.Kinds.Count == 0)
            return null;
        var on = choices
            .Where(c =>
                request.Kinds.Contains(c.Kind)
                && c.Options.Any(o => o.IsCandidate(request.IncludeUnowned) && !Choices.Same(o, c.Current))
            )
            .ToList();
        return on.Count == 0
            ? null
            : new SetupPasses(new ChoiceSteps(on, request.IncludeUnowned), choices, request, share, prices);
    }

    /// <summary>
    /// After a race over every gear set: a climb over the choices from its winner (current gear when none won, the
    /// one that died least when none is within the death limit).
    /// </summary>
    public Climb After(
        IReadOnlyList<StageResult> results,
        IReadOnlyList<Candidate> candidates,
        int fewestDeaths,
        GearCatalogue catalogue,
        SearchPlan plan
    )
    {
        var end = Race.End(results, _request.Goal, _share, _prices);
        var winner = end.State switch
        {
            AnswerState.Found when end.Best is { } best => best,
            AnswerState.NoneWithinLimit => fewestDeaths,
            _ => 0,
        };
        Noted(end, i => i, candidates, winner);
        Log.Info(
            $"setup search: gear raced, {(winner == 0 ? "current gear" : candidates[winner].Changes.ShortForm)} holds while {Steps.Choices.Count} choices are tried"
        );
        return Climb.FromSet(catalogue, plan.Branches, candidates[winner].Changes, Steps);
    }

    /// <summary>
    /// Completes the answer the search ended with: the tie order, Cheaper and Safer, and each set's choices and
    /// supplies. A cancelled or failed search has none of it.
    /// </summary>
    public void Finish(
        Answer answer,
        IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<int, StageResult> all,
        GearCatalogue? catalogue,
        PriceTable prices
    )
    {
        if (answer.State is AnswerState.Cancelled or AnswerState.Failed)
            return;
        answer.StoppedBeforeCloseCalls = answer.StoppedAtStage != null && Races.Any;
        SetupAnswers.FillChoices(answer, _all, Steps.Choices, candidates, all, catalogue, prices);
        var game = Game.LiveGame.Find();
        SetupAnswers.NameSupplies(answer, id => game == null ? Ids.Brief(id) : Game.LiveGame.NameOf(game, id));
    }

    /// <summary>A step's sets from the best to the worst by the goal, those without a value last.</summary>
    public IReadOnlyList<ChangeList> Order(IReadOnlyList<StageResult> results, IReadOnlyList<Candidate> candidates)
    {
        var lower = Ranking.LowerIsBetter(_request.Goal.Metric);
        return
        [
            .. results
                .Select(r => (r.Index, Value: Race.Value(r, _prices)))
                .OrderBy(p => p.Value == null ? 1 : 0)
                .ThenBy(p => p.Value is { } v ? (lower ? v.Point : -v.Point) : 0)
                .ThenBy(p => p.Index)
                .Select(p => candidates[p.Index].Changes),
        ];
    }
}
