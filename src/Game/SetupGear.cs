using GamesByMalcs.Melvor2.Components.Loadouts;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Common;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A setup's equipped items (in slot order), combat style, attack stance and selected food (item id).
/// </summary>
internal sealed record SetupGear(IReadOnlyList<string> Items, string? Style, string? Stance, string? Food)
{
    public static readonly SetupGear None = new([], null, null, null);

    /// <summary>
    /// What a combat entity wears and how it fights: as it is now, or as one of its stored loadouts would (the game
    /// keeps each loadout it isn't wearing in its equipment, food and player components). A stored loadout without
    /// a stance of its own keeps the current one, as swapping to it would. Empty parts if they can't be read.
    /// </summary>
    public static SetupGear Read(CombatEntity combat, LoadoutSlot? stored)
    {
        if (!Dependencies.IsAvailable(Dependencies.Gear))
            return None;
        try
        {
            var equipment = combat.Player.Equipment;
            var slots = equipment._activeSlots.OrderBy(s => s.Value.GridPosition.Y).ThenBy(s => s.Value.GridPosition.X);
            List<string> items;
            string? food;
            var stance = combat.Player.Stats.AttackStance.Value;
            if (stored == null)
            {
                items = [.. slots.Where(s => s.Value.Equipped != null).Select(s => s.Value.Equipped!.Item.Id)];
                food = combat.Food.SelectedFood?.Item.Id;
            }
            else
            {
                var loadout = equipment.Loadouts.GetValueOrDefault(stored);
                items =
                [
                    .. slots
                        .Select(s => loadout?.Slots.GetValueOrDefault(s.Key)?.Equipped)
                        .Where(e => e != null)
                        .Select(e => e!.Item.Id),
                ];
                food = combat.Food.Loadouts.GetValueOrDefault(stored)?.Item.Id;
                if (combat.Player.LoadoutStances.TryGetValue(stored, out var kept))
                    stance = kept;
            }
            return new SetupGear(
                [.. items.Distinct(StringComparer.Ordinal)], // a two-handed weapon fills two slots
                stance.CombatStyle.DisplayName.Text,
                stance.DisplayName.Text,
                food
            );
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or KeyNotFoundException)
        {
            Log.WarnOnce("the setup's gear couldn't be read", e);
            return None;
        }
    }
}
