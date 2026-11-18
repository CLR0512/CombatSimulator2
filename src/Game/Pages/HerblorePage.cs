using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Herblore;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Herblore: the combat potions, the items that fit the toolbelt's combat potion slot, a group per potion as the
/// Herblore recipes group them (its potencies in the recipes' order), then each of the slot's items no recipe makes.
/// One potion per side: ticking one puts it in the slot. An entry's hover is what spends a charge, in the game's
/// words.
/// </summary>
internal static class HerblorePage
{
    private static readonly ChangeKey Key = new(ChangeKind.Potion, null);

    /// <summary>
    /// The save's potion, and how many of each potion the bank holds, read before the side's changes: the potion
    /// as its place in <see cref="Potions"/> from 1 (0 for none) under the slot's key, each count under the
    /// potion's id. A changed side's copy holds potions it was given, so neither can be read from it.
    /// </summary>
    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        if (!Dependencies.IsAvailable(Dependencies.Herblore))
            return;
        var potions = Potions(game);
        var held = Held(game);
        values[Key] = held == null ? 0 : potions.IndexOf(held) + 1;
        var bank = game.Bank.Inventory;
        foreach (var potion in potions)
            values[new ChangeKey(ChangeKind.Potion, potion.Id)] = (int)Math.Min(bank.GetQuantity(potion), int.MaxValue);
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (!Dependencies.IsAvailable(Dependencies.Herblore))
            return PageReader.Unsupported(SetupPage.Herblore);
        var potions = Potions(game);
        if (potions.Count == 0)
            return PageReader.Page(SetupPage.Herblore, PageState.NoData, Note.Of("No combat potions"));
        var held = Held(game);
        var saved = save.GetValueOrDefault(Key) is var at and > 0 && at <= potions.Count ? potions[at - 1] : null;
        var recipes = game.Data.GetDataRegistryOutput<HerbloreRecipe>().GetAllObjects().ToList();
        var groups = new List<PageGroup>();
        var placed = new HashSet<Item>();
        foreach (var group in game.Data.GetDataRegistryOutput<HerblorePotionGroup>().GetAllObjects())
        {
            var made = recipes
                .Where(r => r.PotionGroup == group)
                .SelectMany(r => r.Products.Select(p => p.Item))
                .Where(item => potions.Contains(item) && placed.Add(item));
            AddGroup(group.Id, group.DisplayName.Text, group.Id, made);
        }
        // An item no recipe makes is a potion of its own.
        foreach (var item in potions.Where(placed.Add).ToList())
            AddGroup(item.Id, ((GamesByMalcs.Melvor2.Data.INamedObject)item).DisplayName.Text, item.Id, [item]);
        return new SetupPage(
            SetupPage.Herblore,
            PageSection.Skills,
            PageState.Changeable,
            Note.Of("The potion in your toolbelt. One per side."),
            [],
            groups
        );

        void AddGroup(string id, string name, string? icon, IEnumerable<Item> items)
        {
            var entries = items
                .Select(item => Entry(item, held, saved, save))
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();
            if (entries.Count > 0)
                groups.Add(new PageGroup(id, name, icon, null, entries));
        }
    }

    // A potion the completion log leaves out shows only to a character whose bank holds one.
    private static PageEntry? Entry(Item item, Item? held, Item? saved, IReadOnlyDictionary<ChangeKey, int> save)
    {
        var inBank = save.GetValueOrDefault(new ChangeKey(ChangeKind.Potion, item.Id));
        if (!ChangeOptions.Offered(item, inBank > 0 || item == saved))
            return null;
        if (!item.Capabilities.TryGet<ToolbeltCapability>(out var capability))
            return null;
        var name = ((GamesByMalcs.Melvor2.Data.INamedObject)item).DisplayName.Text;
        var uses = (capability.ConsumesOn ?? [])
            .Select(t => Targets.Plain(t.GetDescription()))
            .Where(t => t.Length > 0)
            .ToList();
        return new PageEntry(
            item.Id,
            name,
            item.Id,
            EntryKind.Toggle,
            item == held ? 1 : 0,
            1,
            item == saved ? 1 : 0,
            Targets.Unmet(capability.Requirements),
            SetupSheets.Describe(ModifierGroup.Toolbelt, item.Id, name, capability.Bonuses),
            Key,
            null,
            Description: uses.Count > 0 ? string.Join("\n", uses) : null,
            Remark: inBank > 0 ? Note.Of("{0} in your bank", inBank) : Note.Of("Not in your bank")
        );
    }

    /// <summary>The build's combat potions in the game's order.</summary>
    public static List<Item> Potions(MelvorGame game) =>
        [
            .. game
                .Data.GetDataRegistryOutput<Item>()
                .GetAllObjects()
                .Where(item =>
                    item.Capabilities.TryGet<ToolbeltCapability>(out var toolbelt)
                    && toolbelt.FitsInSlot.Id == Changes.CombatPotionSlot
                ),
        ];

    // The potion in the combat potion slot of the game's combat entity.
    private static Item? Held(MelvorGame game) =>
        PotionChange.Slot(LiveGame.Combat(game)) is { } slot
        && LiveGame.Combat(game).Toolbelt.TryGetActiveSlot(slot, out var active)
            ? active.Equipped?.Item
            : null;
}
