using System.Security.Cryptography;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Saves;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>A hash per part of the live game at one moment (compared against a control window).</summary>
internal sealed record Fingerprint(string Id, DateTime TakenAt, ulong Frame, SortedDictionary<string, string> Parts);

/// <summary>
/// Each entity and each of its components written through its own save writer and hashed, plus the RNG, action
/// managers and event log. On the live game (main thread, Debug builds), comparing a simulation window's differing
/// parts against a control window's shows whether CS2 changed anything. On a copy (its own thread), comparing the
/// parts after a reset with those after preparing shows whether the reset put everything the save holds back.
/// </summary>
internal static partial class Fingerprints
{
    /// <summary>The part holding the game's RNG, which every fight reseeds.</summary>
    public const string Rng = "rng";

    /// <summary>The part holding the active action manager.</summary>
    public const string Actions = "actions";

    /// <summary>The part name of an entity's component.</summary>
    public static string ComponentPart(GameEntity entity, string component) =>
        $"component {Ids.Brief(entity.Id)}.{component}";

    /// <summary>
    /// The parts of <paramref name="game"/>, by name, leaving out <paramref name="excluded"/> (and each entity as a
    /// whole when <paramref name="entities"/> is false). Run it on the thread that owns the game.
    /// </summary>
    public static SortedDictionary<string, string> Parts(
        MelvorGame game,
        IReadOnlySet<string>? excluded = null,
        bool entities = true
    ) => Parts(game, name => excluded == null || !excluded.Contains(name), entities);

    /// <summary>The parts of <paramref name="game"/> that <paramref name="include"/> keeps.</summary>
    public static SortedDictionary<string, string> Parts(MelvorGame game, Func<string, bool> include, bool entities)
    {
        var parts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Add(string name, Action<SaveWriter> write)
        {
            if (include(name))
                parts[name] = Hash(game, write);
        }
        foreach (var entity in game.Entities.GetAllObjects())
        {
            if (entities)
                Add("entity " + Ids.Brief(entity.Id), w => w.Write((IWritable)entity));
            // Each component too, so a part that changes by itself (HP regeneration) can't hide one beside it.
            foreach (var (name, component) in entity.Components)
                Add(ComponentPart(entity, name), w => w.Write(component));
        }
        Add(Rng, w => w.Write(game.RNG));
        Add(Actions, w => w.Write(game.ActionManager));
        Add("passive", w => w.Write(game.PassiveActionManager));
        Add("eventlog", w => w.Write(game.EventLog));
        return parts;
    }

    /// <summary>
    /// Parts that change on their own while the player plays and that no fight reads: the RNG, the time of day,
    /// records (trackers, loot histories, statistics), the progress of non-combat actions and passive actions, the
    /// action managers and the event log. Left out of a setup's content hash so a repeat run can find it unchanged.
    /// </summary>
    internal static bool SelfTicking(string part) =>
        part is Rng or Actions or "passive" or "eventlog" or "component TimeAndWeather.TimeOfDay"
        || part.EndsWith(".Tracker", StringComparison.Ordinal)
        || part.EndsWith(".LootHistory", StringComparison.Ordinal)
        || part.EndsWith(".Statistics", StringComparison.Ordinal)
        || part.EndsWith(".Passive", StringComparison.Ordinal)
        || (part.EndsWith(".Action", StringComparison.Ordinal) && part != "component Combat.Action");

    /// <summary>
    /// A hash of the live game's setup content: every part but the self-ticking ones, and each character without
    /// its tick counter, so it stays the same while nothing that can reach a fight changes. Main thread.
    /// </summary>
    public static string SetupHash(MelvorGame game)
    {
        MainThread.Assert("Fingerprints.SetupHash");
        var parts = Parts(game, p => !SelfTicking(p), entities: false);
        // A character counts its own ticks, 60 a second even when idle, and only the time between two of its
        // moments ever reaches a fight (a kill's length). Hashed without that counter, an idle character's setup
        // holds still.
        foreach (var entity in game.Entities.GetAllObjects())
        {
            foreach (var (name, component) in entity.Components)
            {
                if (component is not CharacterComponent character || !parts.ContainsKey(ComponentPart(entity, name)))
                    continue;
                var bytes = Bytes(game, w => w.Write(component));
                if (ByteMask.ZeroOnce(bytes, character.CurrentTick))
                    parts[ComponentPart(entity, name)] = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
            }
        }
        var text = string.Join('\n', parts.Select(p => p.Key + "=" + p.Value));
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static string Hash(MelvorGame game, Action<SaveWriter> write) =>
        Convert.ToHexStringLower(SHA256.HashData(Bytes(game, write)))[..16];

    private static byte[] Bytes(MelvorGame game, Action<SaveWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new SaveWriter(stream, LiveGame.SaveVersion, null, game.Data))
        {
            write(writer);
            writer.Flush();
        }
        return stream.ToArray();
    }
}
