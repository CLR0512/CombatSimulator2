using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Data.Items;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A combat toolbelt slot: an item put in it, or the slot emptied, through the toolbelt's own equip calls. Copies
/// only.
/// </summary>
internal static class ToolbeltChange
{
    // A stack the game uses up is added at this count; the copy raises equipped stacks further when it is prepared.
    private const int ChargesAdded = 1000;

    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements) =>
        copy.Game.Data.TryGetObject<ToolbeltSlot>(change.Subject!, out var slot)
            ? Into(copy, change, slot, ignoreRequirements)
            : Changes.Missing(change, null);

    /// <summary>The change's item put in <paramref name="slot"/>, or the slot emptied for none.</summary>
    public static ChangeOutcome Into(Copy copy, Change change, ToolbeltSlot slot, bool ignoreRequirements)
    {
        var toolbelt = copy.Combat.Toolbelt;
        if (!toolbelt.TryGetActiveSlot(slot, out var active))
            return Changes.Refused(change, null, Note.Of("not a combat toolbelt slot"));
        var from = active.Equipped?.Item.Id ?? Change.None;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (change.Item is not { } itemId)
        {
            copy.Combat.ToolbeltEquipping.UnequipSlot(slot);
            return active.Equipped == null
                ? new(change, from, ChangeState.Accepted, [])
                : Changes.Refused(change, from, Note.Of("the game didn't take it off"));
        }
        if (!copy.Game.Data.TryGetObject<Item>(itemId, out var item))
            return Changes.Missing(change, from);
        if (!item.Capabilities.TryGet<ToolbeltCapability>(out var capability) || capability.FitsInSlot != slot.FitsType)
            return Changes.Refused(change, from, Note.Of("doesn't fit this slot"));
        var reasons = Targets.Unmet(capability.Requirements);
        reasons.AddRange(Targets.Unmet(slot.UnlockRequirements));
        if (reasons.Count > 0 && !ignoreRequirements)
            return Changes.Refused(change, from, [.. reasons]);
        if (toolbelt.IsItemEquipped(item))
            return Changes.Refused(change, from, Note.Of("already in another slot"));
        var quantity = capability.CanEquipQuantity ? ChargesAdded : 1;
        if (!Supplies.Item(copy, item, quantity))
            return Changes.Refused(change, from, Note.Of("bank full"));
        var ignored = ignoreRequirements && reasons.Count > 0;
        using (
            ignored
                ? RequirementsIgnored.Enter(capability.Requirements, slot.UnlockRequirements)
                : default(RequirementsIgnored.Scope?)
        )
            copy.Combat.ToolbeltEquipping.EquipItem(item, quantity, slot);
        var applied = active.Equipped?.Item == item;
        if (!applied && reasons.Count == 0)
            reasons.Add(Note.Of("the game didn't equip it"));
        return Changes.Outcome(change, from, applied, reasons, ignored);
    }
}
