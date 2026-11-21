using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.SlayerTasks.Conditions;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Modifiers;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Game.Search;

/// <summary>
/// Reads everything a search chooses from out of a copy prepared with the setup: the slots, every wearable item
/// as its parts, the set bonuses, and what is worn. Requirements are judged in the copy, so with the setup's
/// levels. Nothing is changed. An item with a part outside <see cref="KnownParts"/> is marked opaque with the
/// part's name; it is then only ever fought, never compared.
/// </summary>
internal static class GearCatalog
{
    public static GearCatalogue Read(Copy copy, TargetSpec target)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(target);
        using var scope = Guards.Enter();
        var game = copy.Game;
        var equipment = copy.Combat.Player.Equipment;
        var owned = Owned(copy, equipment);
        var worn = new Dictionary<Item, string>();
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        var slots = new List<GearSlot>();
        var active = equipment._activeSlots.Values.ToList();
        var minX = active.Count == 0 ? 0 : active.Min(s => s.GridPosition.X);
        var minY = active.Count == 0 ? 0 : active.Min(s => s.GridPosition.Y);
        foreach (var slot in equipment.Config.EquipmentSlots)
        {
            var place = active.FirstOrDefault(s => s.Slot == slot);
            slots.Add(
                new GearSlot(
                    slot.Id,
                    slot.FitsType.Id,
                    slot.UnlockRequirements.AreMet,
                    place == null ? 0 : place.GridPosition.X - minX,
                    place == null ? 0 : place.GridPosition.Y - minY
                )
            );
            // A slot another item takes holds that item too, but only its own slot wears it.
            if (place is { IsOccupied: false, Equipped: { } equipped })
            {
                worn[equipped.Item] = slot.Id;
                current[slot.Id] = equipped.Item.Id;
            }
        }
        var sets = game.Data.GetDataRegistryOutput<EquipmentSetBonus>().GetAllObjects().ToList();
        var groups = new Dictionary<string, PartGroup>(StringComparer.Ordinal);
        var sources = ItemSources.All(game);
        var reachable = ItemSources.Reachable(game, sources);
        var offTask = OffTask(copy, target);
        var items = new List<GearItem>();
        foreach (var item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
        {
            if (
                !item.Capabilities.TryGet<EquipmentCapability>(out var equip)
                || !ChangeOptions.Offered(item, owned.ContainsKey(item) || worn.ContainsKey(item))
            )
                continue;
            var parts = new Parts(offTask);
            parts.Stats(equip.Stats, groups);
            parts.Modifiers(equip.Bonuses);
            if (KnownParts.IsUnknown(item.Id))
                parts.MakeOpaque("a part made unknown");
            item.Capabilities.TryGet<WeaponCapability>(out var weapon);
            items.Add(
                new GearItem(item.Id, LiveGame.NameOf(game, item.Id), equip.Type.Id)
                {
                    Occupies = [.. (equip.OccupiesSlots ?? []).Select(s => s.Id)],
                    Owned = owned.GetValueOrDefault(item),
                    Worn = worn.GetValueOrDefault(item),
                    Unmet = Targets.Unmet(equip.Requirements),
                    Stats = parts.StatValues,
                    Bonuses = parts.Bonuses,
                    Attacks = [.. (equip.Attacks ?? []).Select(a => $"{a.Attack.Id}@{a.Chance:R}")],
                    Effects = parts.Effects,
                    ConsumedOn = [.. (equip.ConsumesOn ?? []).Select(t => t.GetType().Name)],
                    Sets = [.. sets.Where(s => s.Items.Contains(item)).Select(s => s.Id)],
                    Weapon =
                        weapon == null || weapon.Type.Stances.Count == 0
                            ? null
                            : new WeaponPart(
                                weapon.Type.Id,
                                Ids.Brief(weapon.Type.Stances[0].CombatStyle.Id),
                                [.. weapon.Type.Stances.Select(s => s.Id)],
                                weapon.DamageType.Id,
                                [.. weapon.Elements.Select(e => e.Id)],
                                weapon.RequiredAmmoType?.Id
                            ),
                    AmmoType = equip.AmmoType?.Id,
                    Stackable = equip.CanEquipQuantity,
                    Source = sources.TryGetValue(item, out var ways) ? ways[0].Name : null,
                    SourceDetail = ways?[0].Detail,
                    SourceNames = ways == null ? [] : [.. ways.Select(s => s.Name.English()).Distinct()],
                    Reachable = reachable.GetValueOrDefault(item),
                    Opaque = parts.Opaque != null,
                    OpaqueReason = parts.Opaque,
                    Unknowns = parts.Unknowns,
                }
            );
        }
        var stances = game
            .Data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Entities.Combat.AttackStance>()
            .GetAllObjects()
            .ToList();
        var unusable = new Dictionary<string, Note>(StringComparer.Ordinal);
        if (copy.Combat.MagicSpells.SelectedSpell == null)
            foreach (var style in stances.Select(s => s.CombatStyle).Where(s => s.UsesSpells).Distinct())
                // Translators: Why a setup search leaves out a combat style that needs a spell: the setup has none, and
                // Spell is switched off under "Also try". Shown after "Magic: skipped, ".
                unusable[Ids.Brief(style.Id)] = Note.Of("switch on Spell under Also try to include it");
        return new GearCatalogue
        {
            StanceReads = stances.ToDictionary(
                s => s.Id,
                s =>
                    (IReadOnlySet<string>)
                        new HashSet<string>(
                            s.CombatStyle.MaxHitFormula.GetCombatStatDependencies()
                                .Append(s.AccuracyStat)
                                .Append(s.CombatStyle.AttackIntervalStat)
                                .Append(s.CombatStyle.MaxWardStat)
                                .Append(s.CombatStyle.ArmourStat)
                                .Append(s.CombatStyle.LifestealStat)
                                .Select(stat => stat.Id),
                            StringComparer.Ordinal
                        ),
                StringComparer.Ordinal
            ),
            EvasionStats = stances.Select(s => s.EvasionStat.Id).ToHashSet(StringComparer.Ordinal),
            TargetReads = TargetReads(game, target),
            UnusableStyles = unusable,
            Slots = slots,
            Items = items,
            SetBonuses = [.. sets.Select(set => SetBonus(set, offTask))],
            Current = current,
            Stance = copy.Combat.Player.Stats.AttackStance.Value.Id,
            // The game gives an enemy without attacks of its own its plain one, so every target can hurt.
            TargetAttacks = true,
            StatGroups = groups,
            Levels = copy
                .Combat.Player.Stats.ModifiedCombatLevels.Dict.OrderBy(l => l.Key.Id, StringComparer.Ordinal)
                .ToDictionary(l => l.Key.Id, l => l.Value.Value),
            Build = LiveGame.GameBuild,
        };
    }

    // A set bonus's steps by piece count, with the parts of them the search has no rule for.
    private static GearSetBonus SetBonus(EquipmentSetBonus set, bool offTask)
    {
        var unknowns = new List<string>();
        var steps = set
            .Bonuses.GroupBy(b => b.ItemCount)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var parts = new Parts(offTask);
                    foreach (var bonus in g)
                        parts.Modifiers(bonus.Modifiers);
                    unknowns.AddRange(parts.Unknowns.Where(u => !unknowns.Contains(u)));
                    return (IReadOnlyList<PartModifier>)parts.Bonuses;
                }
            );
        return new GearSetBonus(set.Id, set.DisplayName.Text, [.. set.Items.Select(i => i.Id)], steps)
        {
            Unknowns = unknowns,
        };
    }

    // The evasion stats the target's enemies read on the character: the one each of their stances names.
    private static HashSet<string>? TargetReads(GamesByMalcs.Melvor2.Game game, TargetSpec target) =>
        Enemies(game, target)
            ?.SelectMany(e => e.Stances.Items)
            .Select(s => s.Stance.EvasionStat.Id)
            .ToHashSet(StringComparer.Ordinal);

    // The target's enemies: its one, or every enemy of the dungeon's waves; null when the game has no such target.
    private static List<GamesByMalcs.Melvor2.Entities.Combat.Enemy>? Enemies(
        GamesByMalcs.Melvor2.Game game,
        TargetSpec target
    )
    {
        if (target.Enemy != null)
            return game.Data.TryGetObject<GamesByMalcs.Melvor2.Entities.Combat.Enemy>(target.Enemy, out var enemy)
                ? [enemy]
                : null;
        return game.Data.TryGetObject<GamesByMalcs.Melvor2.Components.CombatAreas.Dungeon>(target.Area, out var dungeon)
            ? [.. dungeon.Enemies.SelectMany(wave => wave.Enemies.Items).Select(e => e.Enemy)]
            : null;
    }

    // Whether no enemy of the target is a slayer task now. The copy keeps the save's tasks and never takes a new one,
    // so a bonus only fighting a task gives can't come into force against this target.
    internal static bool OffTask(Copy copy, TargetSpec target) =>
        Enemies(copy.Game, target) is { } enemies
        && (copy.Combat.SlayerTasks is not { } tasks || !enemies.Any(tasks.HasTaskForEnemy));

    // How many of each item the character has: in the bank, worn, and held by the other loadouts, which keep their
    // gear out of the bank.
    private static Dictionary<Item, int> Owned(Copy copy, EquipmentComponent equipment)
    {
        var owned = new Dictionary<Item, int>();
        void Add(Item item, int quantity) => owned[item] = owned.GetValueOrDefault(item) + Math.Max(1, quantity);
        foreach (var stack in copy.Bank.Inventory.Stacks)
            if (stack.Quantity > 0)
                Add(stack.Item, stack.Quantity);
        foreach (var slot in equipment._activeSlots.Values)
            if (slot is { IsOccupied: false, Equipped: { } equipped })
                Add(equipped.Item, slot.Quantity);
        foreach (var loadout in equipment.Loadouts.Values)
        foreach (var slot in loadout.Slots.Values)
            if (slot is { IsOccupied: false, Equipped: { } stored })
                Add(stored.Item, slot.Quantity);
        return owned;
    }

    // One item's or set step's parts as the search's records, with the first part it has no rule for. Off a slayer
    // task, a modifier that only fighting one gives is left out: it never comes into force.
    private sealed class Parts(bool offTask)
    {
        public Dictionary<string, double> StatValues { get; } = new(StringComparer.Ordinal);
        public List<PartModifier> Bonuses { get; } = [];
        public List<ItemEffect> Effects { get; } = [];
        public string? Opaque => Unknowns.Count > 0 ? Unknowns[0] : null;

        /// <summary>Every part the search has no rule for, in the order read, each once.</summary>
        public List<string> Unknowns { get; } = [];

        public void MakeOpaque(string why)
        {
            if (!Unknowns.Contains(why))
                Unknowns.Add(why);
        }

        public void Stats(
            GamesByMalcs.Melvor2.Components.CombatStats.CombatStatValues stats,
            Dictionary<string, PartGroup> groups
        )
        {
            foreach (var value in stats.Stats)
            {
                var id = value.Stat.Id;
                if (id == KnownParts.AttackInterval)
                {
                    // The one stat that is better lower, so it is carried with its direction like a modifier.
                    Bonuses.Add(new PartModifier("stat " + id, null, value.Value, Direction.Bad, PartGroup.Fight));
                    continue;
                }
                if (
                    !KnownParts.StatCategories.TryGetValue(value.Stat.Category.Id, out var group)
                    && !KnownParts.OtherStats.TryGetValue(id, out group)
                )
                {
                    MakeOpaque("stat " + id);
                    group = PartGroup.Other;
                }
                groups[id] = group;
                StatValues[id] = StatValues.GetValueOrDefault(id) + value.Value;
            }
            foreach (var power in stats.PowerRatings)
                Rating("power " + power.DamageType.Id, power.Value, PartGroup.Fight, groups);
            foreach (var protection in stats.ProtectionRatings)
                Rating("protection " + protection.DamageType.Id, protection.Value, PartGroup.Defence, groups);
        }

        private void Rating(string id, double value, PartGroup group, Dictionary<string, PartGroup> groups)
        {
            groups[id] = group;
            StatValues[id] = StatValues.GetValueOrDefault(id) + value;
        }

        public void Modifiers(ModifierValues? modifiers)
        {
            if (modifiers == null)
                return;
            foreach (var value in modifiers.Component)
            {
                if (offTask && value.Condition is FightingSlayerTaskCondition)
                    continue;
                var key = $"{value.Stat.StatsClassId}.{value.Stat.Name}";
                var known = KnownParts.Stats.TryGetValue(key, out var group);
                var direction = Of(value.Stat.Kind);
                if (
                    ModifierRule.Opaque(
                        key,
                        known,
                        direction,
                        value.Condition != null,
                        value.Scaling != null,
                        value.IsEnemy
                    ) is
                    { } why
                )
                    MakeOpaque(why);
                Bonuses.Add(
                    new PartModifier(
                        key,
                        ModifierRule.Context(Context(value), Condition(value.Condition)),
                        value.Value,
                        direction,
                        known ? group : PartGroup.Other,
                        value.Condition?.IsMet ?? true
                    )
                );
            }
            foreach (var value in modifiers.Global)
            {
                if (offTask && value.Condition is FightingSlayerTaskCondition)
                    continue;
                var known = KnownParts.GlobalGroups.TryGetValue(value.StatGroup, out var group);
                var direction = Of(value.Description?.Kind ?? ModifierKind.Neutral);
                if (
                    ModifierRule.Opaque(
                        value.StatGroup,
                        known,
                        direction,
                        value.Condition != null,
                        value.Scaling != null,
                        onEnemy: false
                    ) is
                    { } why
                )
                    MakeOpaque(why);
                Bonuses.Add(
                    new PartModifier(
                        value.StatGroup,
                        ModifierRule.Context(null, Condition(value.Condition)),
                        value.Value,
                        direction,
                        known ? group : PartGroup.Other,
                        value.Condition?.IsMet ?? true
                    )
                );
            }
            foreach (var value in modifiers.CombatEffect)
            {
                var effect = value.Modifier.Effect.Data;
                var trigger = value.Modifier.Trigger;
                if (!KnownParts.Knows("effect kind", effect.GetType()))
                    MakeOpaque("effect kind " + effect.GetType().Name);
                else if (!KnownParts.Knows("effect trigger", trigger.GetType()))
                    MakeOpaque("effect trigger " + trigger.GetType().Name);
                Effects.Add(
                    new ItemEffect(
                        $"{effect.GetType().Name} {effect.Id} on {value.Modifier.AppliesTo}",
                        $"{trigger.GetType().Name} {trigger.Description} at {value.Chance:R}{(value.Condition == null ? "" : " if " + value.Condition.GetType().Name)}"
                    )
                );
            }
        }

        // What narrows a modifier (a style, a stance, a damage type, an effect), so that two values of one stat with
        // different narrowing are never added together. Null when it applies to the stat as a whole.
        private static string? Context(ComponentModifierValue value)
        {
            var text = Targets.Plain(value.GetDescription(1));
            return string.IsNullOrWhiteSpace(text)
                ? null
                : System.Text.RegularExpressions.Regex.Replace(text, @"[-+]?\d+([.,]\d+)?%?", "#");
        }

        // A condition as the game words it, with its kind: two conditions alike in both are the same condition.
        private static string? Condition(GamesByMalcs.Melvor2.Conditions.Condition? condition) =>
            condition == null
                ? null
                : condition.GetType().Name + " " + Targets.Plain(condition.GetDescription(false, false));

        private static Direction Of(ModifierKind kind) =>
            kind switch
            {
                ModifierKind.Positive => Direction.Good,
                ModifierKind.Negative => Direction.Bad,
                _ => Direction.Neutral,
            };
    }
}
