using GamesByMalcs.Melvor2.Entities.Agility;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// An Agility technique's level, set with the game's own XP call as a skill's level is; the places whose entry needs
/// a technique level follow it through the game's conditions. Raising a technique the side hasn't unlocked is refused
/// while requirements are checked. Copies only.
/// </summary>
internal static class TechniqueChange
{
    public const string AgilityId = "MelvorBase:Agility";

    /// <summary>The game's techniques with their levels; none when the build has no Agility.</summary>
    public static IReadOnlyCollection<ActiveAgilityTechnique> Techniques(MelvorGame game) =>
        game.Entities.TryGetObjectById(AgilityId, out var entity) && entity is AgilityEntity agility
            ? [.. agility.Techniques.ActiveTechniques.Values]
            : [];

    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements)
    {
        if (Techniques(copy.Game).FirstOrDefault(t => t.Technique.Id == change.Subject) is not { } active)
            return Changes.Missing(change, null);
        var experience = active.Experience;
        var from = experience.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var level = change.TechniqueLevel!.Value;
        if (level == experience.Level)
            return new(change, from, ChangeState.NoChange, []);
        if (level > experience.LevelCap)
            return Changes.Refused(
                change,
                from,
                Note.Of("level must be {0}–{1}", Change.LowestLevel, experience.LevelCap)
            );
        var reasons = level > experience.Level ? Targets.Unmet(active.Technique.UnlockRequirements) : [];
        if (reasons.Count > 0 && !ignoreRequirements)
            return Changes.Refused(change, from, [.. reasons]);
        experience.SetXp(experience.Table.GetXpForLevel(level));
        return experience.Level == level
            ? Changes.Outcome(change, from, true, reasons, ignoreRequirements)
            : Changes.Refused(change, from, Note.Of("the game kept level {0}", experience.Level));
    }
}
