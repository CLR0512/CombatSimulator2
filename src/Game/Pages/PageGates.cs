using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.GameMenus.Pages;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// What keeps the player from a skill's page in this build: the requirements of the skill's entry in the game's
/// navigation, where a build puts the lock for content it leaves out. Read from the game it is given, so a copy
/// answers for the copy.
/// </summary>
internal static class PageGates
{
    /// <summary>The requirements on the navigation entry of the skill <paramref name="entityId"/>; null for none.</summary>
    public static ConditionGroup? Requirements(MelvorGame game, string entityId) =>
        game
            .Data.GetDataRegistryOutput<SkillNavigationOption>()
            .GetAllObjects()
            .FirstOrDefault(option => option.Entity.Id == entityId)
            ?.Requirements;

    /// <summary>
    /// The unmet requirements of the skill's page, each in the game's own words; empty when the page is open.
    /// </summary>
    public static List<Note> Unmet(MelvorGame game, string entityId)
    {
        var notes = new List<Note>();
        if (Requirements(game, entityId) is not { } group)
            return notes;
        foreach (var condition in group.Conditions)
        {
            if (!condition.IsMet)
                notes.Add(Note.Text(Targets.Describe(condition)));
        }
        return notes;
    }
}
