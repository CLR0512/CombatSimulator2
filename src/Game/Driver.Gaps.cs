using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Modifiers;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

// What of the setup and target a fight can't judge, read from what it wears, prays and holds before it starts.
internal static partial class Driver
{
    private static List<GapNote>? NotSimulated(Copy copy, TargetSpec target)
    {
        var game = copy.Game;
        var combat = copy.Combat;
        var parts = new List<(string Owner, string Name, ModifierValues? Modifiers)>();
        foreach (var slot in combat.Player.Equipment._activeSlots.Values)
            if (
                slot is { IsOccupied: false, Equipped: { } worn }
                && worn.Item.Capabilities.TryGet<EquipmentCapability>(out var equipment)
            )
                parts.Add(("item:" + worn.Item.Id, LiveGame.NameOf(game, worn.Item.Id), equipment.Bonuses));
        foreach (var prayer in combat.Prayer.ActivePrayers.Keys.Where(combat.Prayer.IsPrayerSelected))
            parts.Add(("prayer:" + prayer.Id, prayer.DisplayName.Text, prayer.Bonuses));
        foreach (var slot in combat.Toolbelt._activeSlots.Values)
            if (slot.Equipped?.Item is { } item && item.Capabilities.TryGet<ToolbeltCapability>(out var toolbelt))
                parts.Add(("toolbelt:" + item.Id, LiveGame.NameOf(game, item.Id), toolbelt.Bonuses));
        // A master's fights are on its task, as is a monster the setup puts on one; any other target is off one when
        // the setup puts it off, or the save has none for it.
        var offTask = target.Task == null && target.SingleMaster == null && Search.GearCatalog.OffTask(copy, target);
        return Gaps.Of(
            parts,
            !offTask ? null
                : target.SingleTask != null ? Gaps.SetupOffTask
                : Gaps.SaveOffTask
        );
    }
}
