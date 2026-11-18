using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Data.Items;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Game.Search;

/// <summary>
/// Reads the choices beyond gear a setup search may try out of a copy prepared with the setup: each kind's options
/// as changes on that setup, with what each needs, whether the character owns it and its unmet requirements, judged
/// in the copy. Nothing is changed.
/// </summary>
internal static class ChoiceCatalog
{
    /// <summary>A choice per kind, and per combat toolbelt slot other than the potion's.</summary>
    public static IReadOnlyList<Choice> Read(Copy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        using var scope = Guards.Enter();
        var choices = new List<Choice> { Prayers(copy), Spells(copy), Imbuements(copy), Food(copy) };
        choices.AddRange(Toolbelt(copy));
        choices.Add(Potions(copy));
        return choices;
    }

    private static Choice Prayers(Copy copy)
    {
        var prayers = copy.Combat.Prayer;
        var current = prayers.ActivePrayers.Keys.Where(prayers.IsPrayerSelected).Select(p => p.Id).ToList();
        var options = new List<ChoiceOption> { new(ChoiceKind.Prayers, [], Choices.PrayerChanges(current, [])) };
        foreach (var prayer in prayers.ActivePrayers.Keys)
        {
            var unmet = Targets.Unmet(prayer.UnlockRequirements);
            options.Add(
                new ChoiceOption(ChoiceKind.Prayers, [prayer.Id], Choices.PrayerChanges(current, [prayer.Id]))
                {
                    Unmet = unmet,
                    Names = [prayer.DisplayName.Text],
                }
            );
        }
        var slots = prayers.ActiveSlots.Values.Count(s => s.UnlockRequirements.AreMet);
        return new Choice(
            ChoiceKind.Prayers,
            null,
            new ChoiceOption(ChoiceKind.Prayers, current, ChangeList.Empty),
            options,
            Math.Max(1, slots)
        );
    }

    private static Choice Spells(Copy copy)
    {
        var magic = copy.Combat.MagicSpells;
        var current = magic.SelectedSpell?.Data.Id;
        var options = magic
            .ActiveSpells.Keys.Select(spell =>
                Cast(ChoiceKind.Spell, spell, new Change(ChangeKind.Spell, null, spell.Id)) with
                {
                    NeedsMagic = true,
                }
            )
            .ToList();
        return new Choice(ChoiceKind.Spell, null, Held(ChoiceKind.Spell, current), options);
    }

    private static Choice Imbuements(Copy copy)
    {
        var magic = copy.Combat.MagicSpells;
        var current = magic.SelectedImbuement?.Data.Id;
        var options = new List<ChoiceOption>
        {
            None(ChoiceKind.Imbuement, new Change(ChangeKind.Imbuement, null, Change.None)),
        };
        options.AddRange(
            magic.ActiveImbuements.Keys.Select(imbuement =>
                Cast(ChoiceKind.Imbuement, imbuement, new Change(ChangeKind.Imbuement, null, imbuement.Id))
            )
        );
        return new Choice(ChoiceKind.Imbuement, null, Held(ChoiceKind.Imbuement, current), options);
    }

    private static ChoiceOption Cast(ChoiceKind kind, BaseMagicSpell spell, Change change)
    {
        var unmet = Targets.Unmet(spell.UnlockRequirements);
        return new ChoiceOption(kind, [spell.Id], ChangeList.Of([change]))
        {
            Unmet = unmet,
            Needs = [spell.Element.Id],
            Names = [spell.DisplayName.Text],
        };
    }

    private static Choice Food(Copy copy)
    {
        var bank = copy.Bank.Inventory;
        var current = copy.Combat.Food.SelectedFood?.Item;
        var options = new List<ChoiceOption> { None(ChoiceKind.Food, new Change(ChangeKind.Food, null, Change.None)) };
        foreach (var item in copy.Game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
            if (
                item.Capabilities.TryGet<FoodCapability>(out _)
                && ChangeOptions.Offered(item, item == current || bank.GetQuantity(item) > 0)
            )
                options.Add(
                    new ChoiceOption(
                        ChoiceKind.Food,
                        [item.Id],
                        ChangeList.Of([new Change(ChangeKind.Food, null, item.Id)])
                    )
                    {
                        Owned = item == current || bank.GetQuantity(item) > 0,
                        Names = [LiveGame.NameOf(copy.Game, item.Id)],
                    }
                );
        return new Choice(ChoiceKind.Food, null, Held(ChoiceKind.Food, current?.Id), options);
    }

    // Each combat toolbelt slot but the potion's: the items that fit it, a locked slot's purchase as unmet.
    private static IEnumerable<Choice> Toolbelt(Copy copy)
    {
        var bank = copy.Bank.Inventory;
        var items = copy.Game.Data.GetDataRegistryOutput<Item>().GetAllObjects().ToList();
        foreach (
            var slot in copy
                .Combat.Toolbelt._activeSlots.Values.Where(s => s.Slot.FitsType.Id != Changes.CombatPotionSlot)
                .OrderBy(s => s.GridPosition.Y)
                .ThenBy(s => s.GridPosition.X)
        )
        {
            var slotUnmet = Targets.Unmet(slot.Slot.UnlockRequirements);
            var current = slot.Equipped?.Item;
            var options = new List<ChoiceOption>
            {
                None(ChoiceKind.Toolbelt, new Change(ChangeKind.Toolbelt, slot.Slot.Id, Change.None)),
            };
            foreach (var item in items)
            {
                if (
                    !item.Capabilities.TryGet<ToolbeltCapability>(out var fits)
                    || fits.FitsInSlot != slot.Slot.FitsType
                    || !ChangeOptions.Offered(item, item == current || bank.GetQuantity(item) > 0)
                )
                    continue;
                var unmet = Targets.Unmet(fits.Requirements);
                options.Add(
                    new ChoiceOption(
                        ChoiceKind.Toolbelt,
                        [item.Id],
                        ChangeList.Of([new Change(ChangeKind.Toolbelt, slot.Slot.Id, item.Id)])
                    )
                    {
                        Owned = item == current || bank.GetQuantity(item) > 0,
                        Unmet = [.. slotUnmet, .. unmet],
                        Names = [LiveGame.NameOf(copy.Game, item.Id)],
                    }
                );
            }
            yield return new Choice(ChoiceKind.Toolbelt, slot.Slot.Id, Held(ChoiceKind.Toolbelt, current?.Id), options);
        }
    }

    // Combat potions as the build has them: a build without any offers nothing of this kind.
    private static Choice Potions(Copy copy)
    {
        var options = copy
            .Game.Data.GetDataRegistryOutput<Item>()
            .GetAllObjects()
            .Where(item =>
                item.Capabilities.TryGet<ToolbeltCapability>(out var fits)
                && fits.FitsInSlot.Id == Changes.CombatPotionSlot
                && ChangeOptions.Offered(item, copy.Bank.Inventory.GetQuantity(item) > 0)
            )
            .Select(item => new ChoiceOption(
                ChoiceKind.Potion,
                [item.Id],
                ChangeList.Of([new Change(ChangeKind.Potion, null, item.Id)])
            )
            {
                Owned = copy.Bank.Inventory.GetQuantity(item) > 0,
                Names = [LiveGame.NameOf(copy.Game, item.Id)],
            })
            .ToList();
        var current = copy
            .Combat.Toolbelt._activeSlots.Values.FirstOrDefault(s => s.Slot.FitsType.Id == Changes.CombatPotionSlot)
            ?.Equipped?.Item.Id;
        return new Choice(ChoiceKind.Potion, null, Held(ChoiceKind.Potion, current), options);
    }

    private static ChoiceOption None(ChoiceKind kind, Change change) => new(kind, [], ChangeList.Of([change]));

    // The setup's own option: what it holds, as no change.
    private static ChoiceOption Held(ChoiceKind kind, string? id) =>
        new(kind, id == null ? [] : [id], ChangeList.Empty);
}
