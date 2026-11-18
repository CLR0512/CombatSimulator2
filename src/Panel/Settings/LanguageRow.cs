using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// The language of CS2's own text: the game's, or one CS2 ships even when the game doesn't offer it. The game's names
/// stay in the game's language either way.
/// </summary>
internal static class LanguageRow
{
    /// <summary>The choice and the language shown, for <c>cs2 ui</c>: <c>language=game|code shown=code</c>.</summary>
    public static string Describe() => $"language={PanelState.Prefs.Language ?? "game"} shown={Tr.Locale}";

    /// <summary>Adds the Language setting to <paramref name="section"/>; a choice applies at once and is saved.</summary>
    public static void Add(Godot.VBoxContainer section)
    {
        var choices = LanguageList.Of(Tr.Languages);
        var chosen = PanelState.Prefs.Language;
        var selected = chosen == null ? 0 : choices.ToList().FindIndex(c => c.Code == chosen) + 1;
        SettingRow.Setting(
            section,
            Tr.Get("Language"),
            Tr.Get("CS2's own text. Game names stay in the game's language."),
            SettingRow.Options(
                "Cs2Language",
                [Tr.Get("Same as the game"), .. choices.Select(c => c.Label)],
                selected,
                i => PanelState.ChooseLanguage(i == 0 ? null : choices[i - 1].Code)
            )
        );
    }
}
