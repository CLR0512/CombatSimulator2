using System.Globalization;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// The panel's state as a bug report carries it: the names a player typed for loadouts and presets masked, so a
/// report posted in public shows none of them.
/// </summary>
internal static class ReportPanel
{
    public static string Masked(string text)
    {
        var loadouts = LiveGame.Find() is { } game
            ? LiveGame.Loadouts(game).Select(l => (Slot: SlotNumber(l.Slot), l.Name))
            : [];
        var presets = PanelState.Presets().List.Select(p => p.Name);
        return NameMask.For(loadouts, presets, []).Apply(text);
    }

    // A loadout slot's number, from its id (Loadout2 is 2); 0 for an id without one.
    private static int SlotNumber(string slot)
    {
        var digits = new string([.. slot.Where(char.IsAsciiDigit)]);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}
