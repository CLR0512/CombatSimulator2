using System.Runtime.CompilerServices;
using System.Text;
using GamesByMalcs.Melvor2.Components.MasteryGuild;
using GamesByMalcs.Melvor2.Saves;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

// Resetting a copy between fights: its pristine fingerprint and save bytes, and putting them back.
internal sealed partial class Copy
{
    /// <summary>
    /// The fingerprint parts a reset check leaves out: the RNG, which every fight reseeds, and the combat loot history
    /// and tracker, records the game keeps for display that no fight reads. Entity parts are left out too: each is
    /// its components, which are checked one by one.
    /// </summary>
    private static readonly IReadOnlySet<string> Unchecked = new HashSet<string>(
        [Fingerprints.Rng, "component Combat.LootHistory", "component Combat.Tracker"],
        StringComparer.Ordinal
    );

    /// <summary>
    /// The copy's fingerprint parts as a reset checks them, taken on the copy's thread: the saved parts, plus the
    /// modifiers in force, which the game derives at load and on events rather than saving.
    /// </summary>
    public SortedDictionary<string, string> Parts()
    {
        using var scope = Guards.Enter();
        var parts = Fingerprints.Parts(Game, Unchecked, entities: false);
        parts[ModifiersPart] = ModifiersHash();
        parts[ConditionsPart] = Conditions.Hash();
        return parts;
    }

    private const string ModifiersPart = "modifiers";
    private const string ConditionsPart = "conditions";

    // Every modifier source with its values and multiplier. Sources and values are the copy's own objects, so their
    // identities are stable for the copy's life: the hash compares one copy with itself, never two copies.
    private string ModifiersHash()
    {
        var entries = new List<string>();
        foreach (var (source, values) in Game.Modifiers.Values)
        {
            foreach (var (value, multiplier) in values)
                entries.Add(
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{RuntimeHelpers.GetHashCode(source)}/{RuntimeHelpers.GetHashCode(value)}={multiplier:R}"
                    )
                );
        }
        entries.Sort(StringComparer.Ordinal);
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries)))
        )[..16];
    }

    /// <summary>The fingerprint taken by <see cref="KeepPristine"/>; null until then.</summary>
    public SortedDictionary<string, string>? Pristine { get; private set; }

    // What a reset can read back, by part name: each component and action manager, with its bytes from its own save
    // writer as they were after Prepare.
    private Dictionary<string, (IReadable Target, byte[] Bytes)>? _kept;
    private HashSet<GamesByMalcs.Melvor2.Data.Items.Item>? _keptDefaultTabs;
    private HashSet<GamesByMalcs.Melvor2.Data.Items.Item>? _keptPlaceholders;

    /// <summary>
    /// Records the prepared copy (its fingerprint and every part's save bytes), so <see cref="Reset"/> can put it
    /// back after a fight. Call it once, right after <see cref="Prepare"/>.
    /// </summary>
    public void KeepPristine()
    {
        using var scope = Guards.Enter();
        var kept = new Dictionary<string, (IReadable, byte[])>(StringComparer.Ordinal);
        foreach (var entity in Game.Entities.GetAllObjects())
        {
            foreach (var (name, component) in entity.Components)
                kept[Fingerprints.ComponentPart(entity, name)] = (component, Write(component));
        }
        kept[Fingerprints.Actions] = (Game.ActionManager, Write(Game.ActionManager));
        _kept = kept;
        _keptDefaultTabs = [.. Bank.Inventory.DefaultItemTabs.Keys];
        _keptPlaceholders = [.. Bank.Inventory.PlaceholderItems];
        Pristine = Parts();
    }

    /// <summary>
    /// Puts a copy that fought back the way <see cref="KeepPristine"/> found it, so it can fight another target:
    /// combat stopped and the area unselected, the bank's default tabs for looted items dropped, then every part that
    /// still differs read back through its own save reader. Returns the parts that still differ afterwards (with any
    /// error), so an empty list means the copy is as prepared and may fight again; otherwise dispose it.
    /// </summary>
    public IReadOnlyList<string> Reset()
    {
        if (Pristine == null || _kept == null || _keptDefaultTabs == null)
            throw new InvalidOperationException("Reset needs KeepPristine first");
        using var scope = Guards.Enter();
        Settle();
        var bank = Bank.Inventory;
        // Loot cleared from a bank with automatic placeholders leaves an empty placeholder stack behind.
        foreach (var item in bank.PlaceholderItems.Where(i => !_keptPlaceholders!.Contains(i)).ToList())
            bank.SetItemAsPlaceholder(item, false);
        foreach (var item in bank.DefaultItemTabs.Keys.Where(i => !_keptDefaultTabs.Contains(i)).ToList())
            bank.DefaultItemTabs.Remove(item);
        var bankPart = Fingerprints.ComponentPart(Game.Bank, bank.Id);
        var guilds = new List<MasteryGuildComponent>();
        foreach (var part in PartDiff.Between(Pristine, Parts()))
        {
            // The bank inventory adds to its lookups as it reads, so it can't be read twice: what fights change in it
            // is put back above, and anything else leaves the part differing.
            if (part == bankPart || !_kept.TryGetValue(part, out var kept))
                continue;
            try
            {
                Rereads.Empty(kept.Target);
                // A goal's reader subscribes it to progress again when it reads it as active: stop it first, so a
                // goal that was still going isn't counted twice.
                if (kept.Target is MasteryGuildComponent guildBefore)
                {
                    foreach (var task in guildBefore.ActiveTasks.Values)
                    {
                        if (task.ActiveGoal is { IsActive: true } goal)
                            Goals.SetInactive(goal);
                    }
                }
                using var reader = new SaveReader(
                    new MemoryStream(kept.Bytes, writable: false),
                    Game.Data,
                    Game.Entities
                );
                kept.Target.ReadFrom(reader);
                Rereads.Derive(kept.Target);
                if (kept.Target is MasteryGuildComponent guild)
                    guilds.Add(guild);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return [$"{part} ({e.GetType().Name}: {e.Message})"];
            }
        }
        using (CollectConditions())
        {
            Conditions.Refresh();
            foreach (var guild in guilds)
                Recount(guild);
            Conditions.Refresh();
        }
        return PartDiff.Between(Pristine, Parts());
    }

    /// <summary>
    /// Brings a guild up to date after a reset: its tasks, points and rank follow goal and task events, and reading
    /// its tasks back raises none. Each task checks its completion again (raising the events that move points), then
    /// the points are counted as the game does when a save loads, and any difference goes through the guild's own
    /// API, so its rank and rank bonuses follow.
    /// </summary>
    private static void Recount(MasteryGuildComponent guild)
    {
        foreach (var task in guild.ActiveTasks.Values)
            task.UpdateCompletion();
        var points = 0;
        foreach (var category in guild.Categories.Values)
        {
            category.ComputeCompletedCount();
            points += category.CompletedTasks * category.Category.BasePoints;
        }
        if (points != guild.CurrentPoints)
            guild.AddPoints(points - guild.CurrentPoints);
    }

    private byte[] Write(IWritable writable)
    {
        using var stream = new MemoryStream();
        using (var writer = new SaveWriter(stream, LiveGame.SaveVersion, null, Game.Data))
        {
            writer.Write(writable);
            writer.Flush();
        }
        return stream.ToArray();
    }
}
