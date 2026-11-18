namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What a side's changes do, for display: the stats that differ from the other sheet and the modifiers only one
/// of the two has. The other sheet is the other side's, or the side's own base when there is one side.
/// </summary>
public sealed record Effect(IReadOnlyList<StatDiff> Stats, IReadOnlyList<ModifierSide> Modifiers, int AllStats)
{
    public static readonly Effect None = new([], [], 0);

    public bool IsEmpty => Stats.Count == 0 && Modifiers.Count == 0;

    public int Added => Modifiers.Count(m => m.InB);

    public int Removed => Modifiers.Count(m => m.InA);

    /// <summary>
    /// The modifiers with the lines that read alike merged, in the order each first appears, with how many each stands
    /// for: an Agility technique's bonus is one modifier for every obstacle it applies on, all with the same text.
    /// </summary>
    public IReadOnlyList<(ModifierSide Side, int Count)> Merged =>
        [
            .. Modifiers
                .GroupBy(m => (m.Line.SourceName, m.Line.Description, m.Line.Kind, m.Line.OnEnemy, m.InA, m.InB))
                .Select(g => (g.First(), g.Count())),
        ];

    public static Effect Of(SetupSheet other, SetupSheet side)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(side);
        var diff = SheetDiff.Of(other, side);
        return new([.. diff.Stats.Where(s => s.Differs)], [.. diff.Modifiers.Where(m => m.OneSide)], diff.Stats.Count);
    }

    /// <summary>
    /// A stat's difference as its values are written ("+1.3s"); null when they don't line up (a name, or a stat only
    /// one sheet has).
    /// </summary>
    public static string? Difference(StatDiff stat)
    {
        ArgumentNullException.ThrowIfNull(stat);
        return stat.A == null || stat.B == null ? null
            : SheetDiff.TextDelta(stat.A.Text, stat.B.Text) is { Length: > 0 } delta ? delta
            : null;
    }
}
