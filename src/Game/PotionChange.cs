using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Data.Items;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The combat potion: an item put in the toolbelt's combat potion slot, or the slot emptied, as a toolbelt change
/// does for its slot. A potion the bank doesn't hold is given to the copy, as what-ifs give items. Copies only.
/// </summary>
internal static class PotionChange
{
    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements)
    {
        if (Slot(copy) is not { } slot)
            return Changes.Refused(change, null, Note.Of("no combat potion slot"));
        if (
            change.Item is { } itemId
            && copy.Game.Data.TryGetObject<Item>(itemId, out var item)
            && !(
                item.Capabilities.TryGet<ToolbeltCapability>(out var capability)
                && capability.FitsInSlot == slot.FitsType
            )
        )
            return Changes.Refused(change, null, Note.Of("not a combat potion"));
        return ToolbeltChange.Into(copy, change, slot, ignoreRequirements);
    }

    /// <summary>The copy's combat potion slot, or null when the build has none.</summary>
    public static ToolbeltSlot? Slot(Copy copy) => Slot(copy.Combat);

    public static ToolbeltSlot? Slot(GamesByMalcs.Melvor2.Entities.Combat.CombatEntity combat) =>
        combat.Toolbelt._activeSlots.Keys.FirstOrDefault(s => s.FitsType.Id == Changes.CombatPotionSlot);
}
