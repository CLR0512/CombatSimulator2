using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>What an entry's row shows beyond the entry itself.</summary>
internal static class EntryShown
{
    /// <summary>
    /// Whether the entry is a pet the player hasn't found while Spoiler guard is on: shown as the game's pet list
    /// shows one, a dark shape with no name and nothing more.
    /// </summary>
    public static bool Hidden(SetupPage page, PageEntry entry) =>
        page.Id == SetupPage.Pets && Spoilers.Current.PetHidden(entry.Id);
}
