using GamesByMalcs.Melvor2.Components.SlayerTasks.Conditions;
using GamesByMalcs.Melvor2.Modifiers;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The parts a fight in CS2 can't judge, each with what leaving it out means: what a modifier does that CS2 never
/// lets happen, and what counts only on a Slayer task the save doesn't have. Every other part is fought as the game
/// fights it. A new kind of gap is added here by name when a game update brings one.
/// </summary>
internal static class Gaps
{
    /// <summary>Modifiers by their stat's key, with what CS2 does instead.</summary>
    public static readonly IReadOnlyDictionary<string, Note> Modifiers = new Dictionary<string, Note>(
        StringComparer.Ordinal
    )
    {
        // Translators: What of a part is left out of the fights: it keeps an item when the player dies, and CS2 never takes one.
        ["MelvorBase:CombatAction.NoDeathPenalty"] = Note.Of("its keeping an item on death, as CS2 never takes one"),
    };

    /// <summary>What a bonus that only works on a Slayer task means here, when the save has none for the monster.</summary>
    public static Note SaveOffTask(string bonus) =>
        // Translators: A part's bonus left out of the fights; {0} is the bonus as the game words it ("+10% damage").
        // The Setup tab's Slayer page can put the monster on task.
        Note.Of(
            "{0}, on a Slayer task only. Your save has no task for this monster; On task, on the Setup tab's Slayer page, counts it.",
            bonus
        );

    /// <summary>What a bonus that only works on a Slayer task means here, when the setup fights off task.</summary>
    public static Note SetupOffTask(string bonus) =>
        // Translators: A part's bonus left out of the fights; {0} is the bonus as the game words it ("+10% damage").
        Note.Of("{0}, on a Slayer task only. This setup fights off task.", bonus);

    /// <summary>
    /// The gaps among <paramref name="parts"/>, each the owner's key, its name and its modifiers; on a target fought
    /// off task, <paramref name="offTask"/> says, of each bonus that counts only on one, why it is left out.
    /// </summary>
    public static List<GapNote>? Of(
        IEnumerable<(string Owner, string Name, ModifierValues? Modifiers)> parts,
        Func<string, Note>? offTask
    )
    {
        List<GapNote>? notes = null;
        foreach (var (owner, name, modifiers) in parts)
        foreach (var value in modifiers?.Component ?? [])
        {
            var note =
                Modifiers.GetValueOrDefault($"{value.Stat.StatsClassId}.{value.Stat.Name}")
                ?? (value.Condition is FightingSlayerTaskCondition ? offTask?.Invoke(Bonus(value)) : null);
            if (note != null && !(notes ??= []).Exists(n => n.Owner == owner && n.What.English() == note.English()))
                notes.Add(new GapNote(owner, name, note));
        }
        return notes;
    }

    // The bonus as the game words it, without the task condition the note says itself.
    private static string Bonus(ComponentModifierValue value) =>
        Targets.Plain(value.GetDescription(1)) is { Length: > 0 } text ? text : "A bonus";
}
