using System.Globalization;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Components.Prayers;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Astrology;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.Quests;
using GamesByMalcs.Melvor2.Entities.Shop;
using MelvorMods.CombatSimulator2.Game.Pages;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Between a setup line's entries and the game's ids. Writing: a side's whole setup read from its check copy, or a
/// preset's changes, each id as its hash, or every id of a kind in full when one of them shares its hash with
/// another id of its registry. Reading: each hash looked up among the ids of its registry in the running build;
/// what comes out is a list of changes for the ordinary check and nothing else.
/// </summary>
internal static partial class LineIds
{
    // A constellation's stars as one bit each, where they fit the line's three bytes.
    private const int MaskBits = 24;

    /// <summary>The running build as a line carries it; 0 when it has no number.</summary>
    public static ulong Build =>
        ulong.TryParse(LiveGame.SteamBuild, NumberStyles.None, CultureInfo.InvariantCulture, out var build) ? build : 0;

    /// <summary>
    /// Every value CS2 can set, as <paramref name="game"/> has it: the combat levels, each equipment and combat
    /// toolbelt slot, the stance, spell, imbuement and food, the prayers that are on, each constellation's stars,
    /// guild ranks above none, quests done, the purchases a fight can tell from their absence, pets found, the
    /// bonfire and technique levels; then the loadouts per monster and slayer task choices of
    /// <paramref name="changes"/>, which no copy holds.
    /// </summary>
    public static List<LineEntry> Setup(MelvorGame game, CombatEntity combat, ChangeList? changes = null)
    {
        var state = SetupState.Read(game, combat);
        var entries = new List<LineEntry>();
        foreach (var level in state.Levels)
            entries.Add(new(ChangeKind.Level, new(IdHash.Of(level.Skill, 2), level.Skill), LineId.None, level.Level));
        foreach (var slot in state.Slots)
            entries.Add(new(ChangeKind.Equip, new(IdHash.Of(slot.Slot, 2), slot.Slot), Item(slot.Item), 0));
        foreach (var slot in state.Toolbelt)
            entries.Add(new(ChangeKind.Toolbelt, new(IdHash.Of(slot.Slot, 2), slot.Slot), Item(slot.Item), 0));
        if (state.Stance != null)
            entries.Add(new(ChangeKind.Stance, LineId.None, Item(state.Stance), 0));
        if (state.Spell != null)
            entries.Add(new(ChangeKind.Spell, LineId.None, Item(state.Spell), 0));
        entries.Add(new(ChangeKind.Imbuement, LineId.None, Item(state.Imbuement), 0));
        entries.Add(new(ChangeKind.Food, LineId.None, Item(state.Food), 0));
        foreach (var prayer in state.Prayers.Where(p => p.On))
            entries.Add(new(ChangeKind.Prayer, new(IdHash.Of(prayer.Prayer, 3), prayer.Prayer), LineId.None, 1));
        foreach (var (master, enemy) in Slayer.SlayerTasks.Blocks(game))
            entries.Add(new(ChangeKind.Blocks, new(IdHash.Of(master, 2), master), Item(enemy), 0));
        // The run plan's own changes leave nothing in the copy to read: they are carried as the setup has them.
        foreach (
            var change in changes?.Where(c =>
                c.Kind is ChangeKind.Loadouts or ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor
            ) ?? []
        )
            entries.AddRange(TaskEntries(change));

        var values = PageReader.Values(game);
        entries.AddRange(Stars(game, values));
        foreach (var (key, value) in values.Where(v => v.Value > 0))
        {
            var subject = key.Subject!;
            switch (key.Kind)
            {
                case ChangeKind.Guild:
                    entries.Add(new(ChangeKind.Guild, new(IdHash.Of(subject, 2), subject), LineId.None, value));
                    break;
                case ChangeKind.Quest:
                    entries.Add(new(ChangeKind.Quest, new(IdHash.Of(subject, 3), subject), LineId.None, 1));
                    break;
                default:
                    break;
            }
        }
        var bought = ShopPage
            .Read(game, values)
            .Groups.SelectMany(g => g.Entries)
            .Where(e => e is { Notable: true, Value: > 0, Change: not null });
        foreach (var purchase in bought)
            entries.Add(new(ChangeKind.Shop, new(IdHash.Of(purchase.Id, 3), purchase.Id), LineId.None, purchase.Value));
        entries.AddRange(PageEntries(game));
        return Written(game, entries);
    }

    // Each pet a skill keeps, found (its bonus on) or not; the bonfire's burning tier, or none when out; each Agility
    // technique's level. In full, as levels are, so the setup is the same on any character.
    private static IEnumerable<LineEntry> PageEntries(MelvorGame game)
    {
        if (Dependencies.IsAvailable(Dependencies.Pets))
            foreach (var pet in PetsPage.Active(game))
                yield return new(
                    ChangeKind.Pet,
                    new(IdHash.Of(pet.Pet.Id, 2), pet.Pet.Id),
                    LineId.None,
                    PetChange.Has(game, pet.Pet) ? 1 : 0
                );
        if (Dependencies.IsAvailable(Dependencies.Firemaking) && BonfireChange.Component(game) is { } bonfire)
        {
            var burning = BonfireChange.Burning(bonfire) ? bonfire.CurrentBonfire!.Id : null;
            yield return new(ChangeKind.Bonfire, LineId.None, Item(burning), 0);
        }
        if (Dependencies.IsAvailable(Dependencies.Agility))
            foreach (var technique in TechniqueChange.Techniques(game).Where(t => t.Experience.Level >= 1))
                yield return new(
                    ChangeKind.Technique,
                    new(IdHash.Of(technique.Technique.Id, 2), technique.Technique.Id),
                    LineId.None,
                    technique.Experience.Level
                );
    }

    private static LineId Item(string? id) => id == null ? LineId.None : new(IdHash.Of(id, 3), id);

    // A constellation with any star on: one bit per star in the game's order, minor stars first.
    private static IEnumerable<LineEntry> Stars(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        foreach (var constellation in game.Data.GetDataRegistryOutput<AstrologyConstellation>().GetAllObjects())
        {
            var nodes = constellation.MinorNodes.Concat(constellation.MajorNodes).ToList();
            var on = nodes.Select(n => values.GetValueOrDefault(new ChangeKey(ChangeKind.Star, n.Id)) > 0).ToList();
            if (!on.Contains(true))
                continue;
            if (nodes.Count <= MaskBits)
            {
                var mask = 0;
                for (var i = 0; i < nodes.Count; i++)
                    mask |= on[i] ? 1 << i : 0;
                yield return new(
                    ChangeKind.Star,
                    new(IdHash.Of(constellation.Id, 2), constellation.Id),
                    LineId.None,
                    mask,
                    StarMask: true
                );
                continue;
            }
            foreach (var node in nodes.Where((_, i) => on[i]))
                yield return new(ChangeKind.Star, new(IdHash.Of(node.Id, 3), node.Id), LineId.None, 0);
        }
    }

    /// <summary>A preset's own changes as entries.</summary>
    public static List<LineEntry> Preset(MelvorGame game, ChangeList changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var entries = new List<LineEntry>();
        foreach (var change in changes)
        {
            if (change.Kind is ChangeKind.Loadouts or ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor)
            {
                entries.AddRange(TaskEntries(change));
                continue;
            }
            var subject = change.Subject is { } id
                ? new LineId(IdHash.Of(id, SubjectBytes(change.Kind)), id)
                : LineId.None;
            entries.Add(
                change.Kind switch
                {
                    ChangeKind.Level => new(change.Kind, subject, LineId.None, change.Level!.Value),
                    ChangeKind.Shop or ChangeKind.Guild => new(change.Kind, subject, LineId.None, change.Number!.Value),
                    ChangeKind.Quest => new(change.Kind, subject, LineId.None, change.Completes ? 1 : 0),
                    ChangeKind.Prayer => new(change.Kind, subject, LineId.None, change.TurnsOn ? 1 : 0),
                    ChangeKind.Star => new(change.Kind, subject, LineId.None, 0),
                    ChangeKind.Pet => new(change.Kind, subject, LineId.None, change.Value == Change.On ? 1 : 0),
                    ChangeKind.Technique => new(change.Kind, subject, LineId.None, change.TechniqueLevel!.Value),
                    ChangeKind.Bonfire => new(change.Kind, LineId.None, Item(change.Tier), 0),
                    ChangeKind.Blocks => new(
                        change.Kind,
                        subject,
                        Item(change.Blocked.Count > 0 ? change.Blocked[0] : null),
                        0
                    ),
                    _ => new(change.Kind, subject, Item(change.Value == Change.None ? null : change.Value), 0),
                }
            );
            // A master's blocks are one entry per enemy, after the first: the first is written above.
            foreach (var enemy in change.Blocked.Skip(1))
                entries.Add(new(change.Kind, subject, Item(enemy), 0));
        }
        return Written(game, entries);
    }

    private static int SubjectBytes(ChangeKind kind) =>
        kind
            is ChangeKind.Level
                or ChangeKind.Guild
                or ChangeKind.Equip
                or ChangeKind.Toolbelt
                or ChangeKind.Blocks
                or ChangeKind.Loadouts
                or ChangeKind.SlayerTaskFor
                or ChangeKind.Pet
                or ChangeKind.Technique
            ? 2
            : 3;

    // Each entry with its ids as hashes, or a kind's ids all in full when one of them shares its hash with another
    // id of its registry: a hash must name one id only.
    private static List<LineEntry> Written(MelvorGame game, List<LineEntry> entries)
    {
        var registries = new Registries(game);
        var inFull = entries
            .Where(e =>
                Clashes(registries.Subjects(e), e.Subject, SubjectSize(e))
                || Clashes(registries.Items(e.Kind), e.Item, 3)
            )
            .Select(e => (e.Kind, e.StarMask))
            .ToHashSet();
        return
        [
            .. entries.Select(e =>
                inFull.Contains((e.Kind, e.StarMask))
                    ? e with
                    {
                        Subject = new LineId(0, e.Subject.Full),
                        Item = new LineId(0, e.Item.Full),
                    }
                    : e with
                    {
                        Subject = new LineId(e.Subject.Hash),
                        Item = new LineId(e.Item.Hash),
                    }
            ),
        ];
    }

    private static int SubjectSize(LineEntry entry) =>
        entry.Kind == ChangeKind.Star ? (entry.StarMask ? 2 : 3) : SubjectBytes(entry.Kind);

    private static bool Clashes(IReadOnlyCollection<string> registry, LineId id, int bytes) =>
        id.Full != null && registry.Any(other => other != id.Full && IdHash.Of(other, bytes) == id.Hash);

    /// <summary>
    /// The changes <paramref name="content"/> stands for in <paramref name="game"/>, and the entries whose id this
    /// build doesn't have (as text for the player). False when such an entry comes from this same build: then the
    /// line itself is wrong, and nothing of it is used.
    /// </summary>
    public static bool TryResolve(MelvorGame game, LineContent content, out ChangeList changes, out List<Note> missing)
    {
        ArgumentNullException.ThrowIfNull(content);
        var registries = new Registries(game);
        var list = new List<Change>();
        var blocks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var loadouts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        missing = [];
        changes = ChangeList.Empty;
        foreach (var entry in content.Entries)
        {
            var subject = Find(registries.Subjects(entry), entry.Subject, SubjectSize(entry));
            var item = Find(registries.Items(entry.Kind), entry.Item, 3);
            if ((!entry.Subject.IsNone && subject == null) || (!entry.Item.IsNone && item == null))
            {
                missing.Add(Note.Of("{0}: not in this build", Change.KindName(entry.Kind)));
                continue;
            }
            if (entry.StarMask)
            {
                var constellation = game.Data.GetObject<AstrologyConstellation>(subject!);
                var nodes = constellation.MinorNodes.Concat(constellation.MajorNodes).ToList();
                for (var i = 0; i < MaskBits; i++)
                {
                    if ((entry.Number & 1 << i) == 0)
                        continue;
                    if (i < nodes.Count)
                        list.Add(new Change(ChangeKind.Star, nodes[i].Id, Change.On));
                    else
                        missing.Add(Note.Of("{0}: not in this build", Change.KindName(entry.Kind)));
                }
                continue;
            }
            if (entry.Kind is ChangeKind.Loadouts or ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor)
            {
                if (TaskChange(registries, entry, subject, item, loadouts, missing) is { } task)
                    list.Add(task);
                continue;
            }
            if (entry.Kind == ChangeKind.Blocks)
            {
                if (!blocks.TryGetValue(subject!, out var enemies))
                    blocks[subject!] = enemies = [];
                if (item != null)
                    enemies.Add(item);
                continue;
            }
            var number = entry.Number.ToString(CultureInfo.InvariantCulture);
            list.Add(
                entry.Kind switch
                {
                    ChangeKind.Level or ChangeKind.Shop or ChangeKind.Guild => new Change(entry.Kind, subject, number),
                    ChangeKind.Quest => new Change(entry.Kind, subject, entry.Number == 1 ? Change.Done : Change.Open),
                    ChangeKind.Prayer => new Change(entry.Kind, subject, entry.Number == 1 ? Change.On : Change.Off),
                    ChangeKind.Star => new Change(entry.Kind, subject, Change.On),
                    ChangeKind.Pet => new Change(entry.Kind, subject, entry.Number == 1 ? Change.On : Change.Off),
                    ChangeKind.Technique => new Change(entry.Kind, subject, number),
                    ChangeKind.Bonfire => new Change(entry.Kind, null, item ?? Change.Out),
                    _ => new Change(entry.Kind, subject, item ?? Change.None),
                }
            );
        }
        foreach (var (master, enemies) in blocks)
            list.Add(
                new Change(ChangeKind.Blocks, master, enemies.Count == 0 ? Change.None : string.Join(',', enemies))
            );
        foreach (var (master, pairs) in loadouts)
            list.Add(new Change(ChangeKind.Loadouts, master, pairs.Count == 0 ? Change.None : string.Join(',', pairs)));
        if (missing.Count > 0 && content.GameBuild == Build)
            return false;
        // Two entries can't name one thing, whatever the line says: a second star entry for a star already on.
        changes = ChangeList.Of(list.DistinctBy(c => c.Key));
        return true;
    }

    // The id the line means: the one given in full if the registry has it, else the one id with that hash.
    private static string? Find(IReadOnlyCollection<string> registry, LineId id, int bytes)
    {
        if (id.IsNone)
            return null;
        if (id.Full != null)
            return registry.Contains(id.Full) ? id.Full : null;
        var matches = registry.Where(other => IdHash.Of(other, bytes) == id.Hash).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    // The ids of each registry a line's entries name, read once per use.
    private sealed class Registries(MelvorGame game)
    {
        private readonly Dictionary<string, IReadOnlyCollection<string>> _ids = new(StringComparer.Ordinal);

        private IReadOnlyCollection<string> Of<T>()
            where T : INamespacedObject
        {
            if (!_ids.TryGetValue(typeof(T).Name, out var ids))
                _ids[typeof(T).Name] = ids = [
                    .. game.Data.GetDataRegistryOutput<T>().GetAllObjects().Select(o => o.Id),
                ];
            return ids;
        }

        private IReadOnlyCollection<string> Skills()
        {
            if (!_ids.TryGetValue("skills", out var ids))
                _ids["skills"] = ids = [.. game.LeveledEntities.Select(e => e.Id)];
            return ids;
        }

        public IReadOnlyCollection<string> Subjects(LineEntry entry) =>
            entry.Kind switch
            {
                ChangeKind.Level or ChangeKind.Guild => Skills(),
                ChangeKind.Quest => Of<Quest>(),
                ChangeKind.Shop => Of<ShopPurchase>(),
                ChangeKind.Star => entry.StarMask ? Of<AstrologyConstellation>() : Of<AstrologyNode>(),
                ChangeKind.Equip => Of<EquipmentSlot>(),
                ChangeKind.Toolbelt => Of<ToolbeltSlot>(),
                ChangeKind.Prayer => Of<Prayer>(),
                ChangeKind.Blocks or ChangeKind.Loadouts =>
                    Of<GamesByMalcs.Melvor2.Components.SlayerTasks.SlayerMaster>(),
                ChangeKind.SlayerTaskFor => Of<GamesByMalcs.Melvor2.Components.CombatAreas.CombatArea>(),
                ChangeKind.Pet => Of<GamesByMalcs.Melvor2.Components.Pets.Pet>(),
                ChangeKind.Technique => Of<GamesByMalcs.Melvor2.Entities.Agility.AgilityTechnique>(),
                _ => [],
            };

        public IReadOnlyCollection<string> LoadoutSlots() => Of<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot>();

        public IReadOnlyCollection<string> Items(ChangeKind kind) =>
            kind switch
            {
                ChangeKind.Stance => Of<AttackStance>(),
                ChangeKind.Spell => Of<MagicSpell>(),
                ChangeKind.Imbuement => Of<Imbuement>(),
                ChangeKind.Equip or ChangeKind.Toolbelt or ChangeKind.Food or ChangeKind.Potion => Of<Item>(),
                ChangeKind.Blocks or ChangeKind.Loadouts or ChangeKind.SlayerTaskFor =>
                    Of<GamesByMalcs.Melvor2.Entities.Combat.Enemy>(),
                ChangeKind.Bonfire => Of<GamesByMalcs.Melvor2.Entities.Firemaking.FiremakingBonfire>(),
                _ => [],
            };
    }
}
