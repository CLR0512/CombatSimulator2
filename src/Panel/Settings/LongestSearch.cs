using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// "Longest gear search": the player's limit on a search a slot at a time, as a list (5, 10, 15, 30 min, no limit)
/// like the run settings beside it, saved with the preferences. In the Settings tab, with its explanation under it,
/// and the ☰ menu, with it on hover; each read again when shown.
/// </summary>
internal sealed class LongestSearch
{
    private readonly Dropdown _choices;

    /// <summary>The row in <paramref name="section"/>, its nodes named from <paramref name="prefix"/>.</summary>
    public LongestSearch(Godot.VBoxContainer section, string prefix, bool help)
    {
        _choices = SettingRow.Options(
            prefix + "SearchLongest",
            [
                .. Preferences.SearchMinuteChoices.Select(m =>
                    // Translators: A choice of the longest a gear search runs: "No limit" runs it to its end; else {0} minutes.
                    m == 0
                        ? Tr.Get("No limit")
                        : Tr.Format("{0} min", Ui.Number(m))
                ),
            ],
            Index(),
            i =>
            {
                PanelState.Prefs.SearchMinutes = Preferences.SearchMinuteChoices[i];
                PanelState.Save();
            }
        );
        var explained = Tr.Get(
            "A search too big to try every set builds one slot by slot; at this limit it keeps the best set found so far."
        );
        // Translators: Setting of the gear search: the longest it runs before keeping the best set found so far.
        SettingRow.Setting(section, Tr.Get("Longest gear search"), help ? explained : "", _choices.Root);
        if (!help)
            _choices.Root.TooltipText = explained;
    }

    /// <summary>The choice as saved.</summary>
    public void Show() => _choices.Select(Index());

    private static int Index() =>
        Math.Max(0, Preferences.SearchMinuteChoices.ToList().IndexOf(PanelState.Prefs.SearchMinutes));
}
