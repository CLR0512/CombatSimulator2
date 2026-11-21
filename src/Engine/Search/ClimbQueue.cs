using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// A search's climbs in their order: one a style when other styles are included, each over gear and choices from
/// current gear, else one. The choices stay in every climb: a prayer or potion held makes every later fight shorter,
/// so the gear's steps cost less with them than without. The player's limit (<see cref="SearchLimit"/>) is shared
/// among them.
/// </summary>
internal sealed class ClimbQueue(SearchRequest request, ChoiceSteps? steps)
{
    private readonly Queue<Climb> _queued = new();

    public SearchLimit Limit { get; } = new(request.MaxSeconds);

    /// <summary>The climbs still to come after the one under way.</summary>
    public int After => _queued.Count;

    /// <summary>
    /// The first climb, <paramref name="from"/> when a race already found the gear, else the first style's;
    /// <paramref name="elapsed"/> seconds into the search.
    /// </summary>
    public Climb Start(SearchPlan plan, GearCatalogue catalogue, Climb? from, double elapsed)
    {
        if (from == null)
            foreach (
                var branches in request.AllStyles
                    ? plan
                        .Branches.GroupBy(b => b.Style)
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => g.ToList())
                    :
                    [
                        [.. plan.Branches],
                    ]
            )
                _queued.Enqueue(new Climb(catalogue, branches, steps));
        var climb = from ?? _queued.Dequeue();
        Limit.ClimbBegins(elapsed, After);
        return climb;
    }

    /// <summary>The next style's climb, or null when every style has climbed.</summary>
    public Climb? NextStyle(double elapsed)
    {
        if (_queued.Count == 0)
            return null;
        var climb = _queued.Dequeue();
        Limit.ClimbBegins(elapsed, After);
        return climb;
    }
}
