namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// The targets a filter shows, the shown ones that are ignored, and the ones a run covers, each in the catalogue's
/// order. One function for the strip, the list, the run button's count and the run itself.
/// </summary>
public sealed record TargetSelection(
    IReadOnlyList<TargetFact> Shown,
    IReadOnlyList<TargetFact> Ignored,
    IReadOnlyList<TargetFact> ToRun
)
{
    /// <summary>
    /// A run covers what is shown and not ignored. "Can fight" leaves out what the build locks; with every target
    /// shown, those are kept and the run refuses them with the reason. An ignored target outside the filter stays
    /// ignored: the set is only read here. Search text in quotes matches a whole name, or an area's name and a
    /// target's as <c>Area · Name</c>, instead of any part of either.
    /// </summary>
    public static TargetSelection Of(
        IReadOnlyList<TargetFact> catalog,
        TargetFilter filter,
        IReadOnlySet<string> ignored
    )
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(ignored);
        var areas = filter.Areas?.ToHashSet(StringComparer.Ordinal);
        var search = filter.Search.Trim();
        var exact = search.Length > 1 && search[0] == '"' && search[^1] == '"' ? search[1..^1].Trim() : null;
        var shown = new List<TargetFact>();
        var waiting = new List<TargetFact>();
        var toRun = new List<TargetFact>();
        foreach (var target in catalog)
        {
            if (
                (target.Tags & filter.Kinds) == 0
                || areas?.Contains(target.Area) == false
                || filter.CanFightOnly && !target.CanFight
                || exact != null && !Named(target, exact)
                || exact == null
                    && search.Length > 0
                    && !target.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    && !target.AreaName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            )
                continue;
            shown.Add(target);
            (ignored.Contains(target.Key) ? waiting : toRun).Add(target);
        }
        return new(shown, waiting, toRun);
    }

    /// <summary>Quoted search text that matches <paramref name="target"/> alone among <paramref name="catalog"/>.</summary>
    public static string ExactSearch(IReadOnlyList<TargetFact> catalog, TargetFact target)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(target);
        var unique = catalog.Count(t => Named(t, target.Name)) == 1;
        return "\"" + (unique ? target.Name : Qualified(target)) + "\"";
    }

    private static bool Named(TargetFact target, string name) =>
        string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Qualified(target), name, StringComparison.OrdinalIgnoreCase);

    private static string Qualified(TargetFact target) => target.AreaName + " · " + target.Name;
}
