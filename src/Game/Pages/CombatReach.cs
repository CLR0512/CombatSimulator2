using GamesByMalcs.Melvor2.Components;
using GamesByMalcs.Melvor2.Modifiers;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Whether bonuses reach a fight or what results count from one, read from what each value modifies: a value that
/// applies everywhere (XP gained among them), a combat effect, or one bound to the combat entity or a combat skill
/// (Slayer among them). A value bound to another skill's entity, such as bank space or a skill's own action,
/// doesn't.
/// </summary>
internal static class CombatReach
{
    public static bool Of(ModifierValues bonuses) =>
        bonuses.Global.Count > 0 || bonuses.CombatEffect.Count > 0 || bonuses.Component.Any(InAFight);

    /// <summary>The values that reach a fight, and the rest; either can be empty.</summary>
    public static (ModifierValues Combat, ModifierValues Other) Split(ModifierValues bonuses) =>
        (
            new ModifierValues([.. bonuses.Global], [.. bonuses.Component.Where(InAFight)], [.. bonuses.CombatEffect]),
            new ModifierValues([], [.. bonuses.Component.Where(v => !InAFight(v))], [])
        );

    /// <summary>Whether a set of values holds nothing.</summary>
    public static bool IsEmpty(ModifierValues bonuses) =>
        bonuses.Global.Count == 0 && bonuses.Component.Count == 0 && bonuses.CombatEffect.Count == 0;

    private static bool InAFight(ComponentModifierValue value) =>
        value.LocalContext.Objects.Any(o =>
            o is IGameComponent component
            && (component.Entity.Id == LiveGame.CombatId || Changes.CombatSkills.Contains(component.Entity.Id))
        );
}
