using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

internal static partial class LiveGame
{
    // The systems a change can touch beyond the gear: a line each, so a difference names where it is.
    private static readonly string[] ChangedSystems =
    [
        ".Toolbelt",
        " Astrology.",
        ".MasteryGuild",
        " Shop.",
        " Quests.",
        " Bank.",
    ];

    // The stars and each constellation's knowledge are kept with the skill's action, which only moves while the
    // character studies.
    private const string StarsPart = "component Astrology.Action";

    // Each master's task, progress, blocks and Auto Slayer: what master runs force and block changes set in copies.
    private const string SlayerTasksPart = "component Slayer.Tasks";

    /// <summary>
    /// What a check or a run must leave as it was in the live game, one line per part: the setup (levels, gear,
    /// stance, spell, food, prayers), then the saved bytes' hash of the toolbelt, astrology, the guilds' tasks, the
    /// shop's purchases, the quests, the bank and the wallet. Parts that tick by themselves are left out.
    /// </summary>
    public static IReadOnlyList<string> StateLines(MelvorGame game)
    {
        var lines = new List<string>(SetupState.Read(game, Combat(game)).Lines());
        var parts = Fingerprints.Parts(
            game,
            part =>
                part is StarsPart or SlayerTasksPart
                || !Fingerprints.SelfTicking(part)
                    && ChangedSystems.Any(system => part.Contains(system, StringComparison.Ordinal)),
            entities: false
        );
        lines.AddRange(parts.Select(part => $"{part.Key} {part.Value}"));
        return lines;
    }

    /// <summary>
    /// What the observe check compares: the frame, kills of the target, XP per skill, items and ammo held, gear.
    /// </summary>
    public static LivePoint Observe(MelvorGame game, TargetSpec target)
    {
        var combat = Combat(game);
        var enemy = game.Data.GetObject<Enemy>(target.Enemy!);
        var xp = game.LeveledEntities.ToDictionary(e => e.Id, e => e.Experience.Xp, StringComparer.Ordinal);
        var held = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var stack in game.Bank.Inventory.Stacks.Where(s => !s.IsPlaceholder))
            held["item:" + stack.Item.Id] = held.GetValueOrDefault("item:" + stack.Item.Id) + stack.Quantity;
        foreach (var (currency, stack) in game.Bank.Wallet.Stacks)
            held["currency:" + currency.Id] = stack.Quantity;
        var gear = new System.Text.StringBuilder();
        var equipment = combat.Player.Equipment;
        foreach (var slot in equipment.Config.EquipmentSlots)
        {
            if (
                equipment.TryGetActiveSlot(slot, out var active)
                && active is GamesByMalcs.Melvor2.Components.Equipment.ActiveEquipmentSlot { Equipped: { } item } a
            )
            {
                held["ammo:" + item.Item.Id] = held.GetValueOrDefault("ammo:" + item.Item.Id) + a.Quantity;
                gear.Append(item.Item.Id).Append(';');
            }
        }
        gear.Append(combat.Loadouts.CurrentLoadout.Data.Id);
        return new LivePoint(
            Godot.Engine.GetPhysicsFrames(),
            combat.Statistics.EnemyKilled.GetValue(enemy),
            xp,
            held,
            gear.ToString()
        );
    }
}

/// <summary>The live game at one moment, for the observe check.</summary>
internal sealed record LivePoint(
    ulong Frame,
    double Kills,
    Dictionary<string, double> Xp,
    Dictionary<string, long> Held,
    string Gear
);
