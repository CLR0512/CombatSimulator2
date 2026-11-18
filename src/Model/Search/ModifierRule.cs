namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// Whether a modifier on an item can be compared, and as what. A modifier the search knows is compared in the
/// group it acts on. One it doesn't know is compared all the same when the game labels it good or bad: it becomes a
/// stat of its own, counted for every goal, in the game's direction. More of a good thing never loses to less of
/// it, so that can't rule out a better set, unless the label misleads: those are listed in <see cref="Misleading"/>
/// and stay opaque, as do modifiers labelled neither. A modifier under a condition is a stat of its own per
/// condition, so items compare on it only when they share it; one that scales with something, or that acts on the
/// enemy, is never compared.
/// </summary>
public static class ModifierRule
{
    /// <summary>
    /// Stats the game labels good or bad whose label can mislead a search, as <c>stats class.name</c>: avoiding or
    /// being immune to an effect (rolled for helpful effects too), a bonus whose meaning is in the stat or skill it
    /// names, a quicker respawn (more kills, but less time to heal between fights), area effects (some may help),
    /// and light and darkness, whose effect on a fight is not checked.
    /// </summary>
    public static IReadOnlySet<string> Misleading { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "MelvorBase:CombatEffect.AvoidanceChance",
            "MelvorBase:Target.ImmunityFrom",
            "MelvorBase:Character.FlatCombatStatValue",
            "MelvorBase:Character.PercentCombatStatValue",
            "MelvorBase:Character.FlatCombatLevel",
            "MelvorBase:CombatAction.FlatSpawnInterval",
            "MelvorBase:CombatAction.PercentSpawnInterval",
            "MelvorBase:CombatAreas.FlatAreaEffectMagnitude",
            "MelvorBase:CombatAreas.DisableAreaEffect",
            "MelvorBase:Character.FlatLight",
            "MelvorBase:Character.FlatDarkness",
            "MelvorBase:Character.PercentDarkness",
        };

    /// <summary>
    /// Why a modifier of <paramref name="key"/> keeps its item from being compared, or null when it doesn't.
    /// <paramref name="known"/> is whether the search has a rule for it.
    /// </summary>
    public static string? Opaque(
        string key,
        bool known,
        Direction direction,
        bool conditional,
        bool scaling,
        bool onEnemy
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!known && (direction == Direction.Neutral || Misleading.Contains(key)))
            return "modifier " + key;
        if (scaling)
            return "scaling modifier " + key;
        if (onEnemy)
            return "modifier on the enemy " + key;
        if (conditional && direction == Direction.Neutral)
            return "conditional modifier " + key;
        return null;
    }

    /// <summary>
    /// What narrows a modifier, with its condition when it has one: two values are added together, and items
    /// compared on them, only when this is the same.
    /// </summary>
    public static string? Context(string? narrowing, string? condition) =>
        condition == null ? narrowing : (narrowing == null ? "" : narrowing + " ") + "if " + condition;
}
