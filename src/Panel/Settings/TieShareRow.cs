using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// The gear search's settings: "Treat as equal within", the margin a set must beat another by to rank above it, as
/// a stepper from 0% to 5% in half steps; the longest a search a slot at a time runs, as four buttons; and clearing the
/// answers kept to show again on returning to a target.
/// </summary>
internal sealed class TieShareRow
{
    private const double Step = 0.005;

    private readonly Button _down;
    private readonly Button _up;
    private readonly Label _value;
    private readonly Button _clear;
    private readonly LongestSearch _longest;

    /// <summary>Adds the rows to <paramref name="section"/>.</summary>
    public TieShareRow(VBoxContainer section)
    {
        var stepper = Ui.Row(4, "SettingsTieShare");
        _down = Ui.Button("−", Look.Tab, "SettingsTieShareDown", () => Set(PanelState.Prefs.TieShare - Step));
        stepper.AddChild(Look.Narrow(_down, 10));
        var box = Look.Panel(Art.SideBox, "SettingsTieShareValue");
        box.CustomMinimumSize = new Vector2(72, 0);
        _value = Ui.Label("", TextStyle.BodyStrong);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_value);
        stepper.AddChild(box);
        _up = Ui.Button("+", Look.Tab, "SettingsTieShareUp", () => Set(PanelState.Prefs.TieShare + Step));
        stepper.AddChild(Look.Narrow(_up, 10));
        SettingRow.Setting(
            section,
            // Translators: Setting of the gear search: sets whose results differ by less than this share count as equal.
            Tr.Get("Treat as equal within"),
            Tr.Get("Gear search: a set must beat another by more than this to rank above it."),
            stepper
        );
        _longest = new LongestSearch(section, "Settings", help: true);
        // Translators: Button that forgets the gear search answers kept for each target.
        _clear = Ui.Button(Tr.Get("Clear kept answers"), Look.Cancel8, "SettingsClearAnswers", Clear);
        SettingRow.Setting(
            section,
            Tr.Get("Gear search answers"),
            Tr.Get("Kept to show again on returning to their target."),
            _clear
        );
        Show();
    }

    /// <summary>Shows the margin and whether there are answers to clear.</summary>
    public void Show()
    {
        var share = PanelState.Prefs.TieShare;
        _value.Text = Ui.Percent(share);
        _down.Disabled = share <= 0;
        _up.Disabled = share >= Preferences.MaxTieShare - 1e-9;
        _clear.Disabled = RunService.KeptAnswerCount == 0;
        _longest.Show();
    }

    /// <summary>The margin as <c>cs2 ui</c> prints it.</summary>
    public static void Describe(System.Text.StringBuilder text) =>
        text.Append(
            CultureInfo.InvariantCulture,
            $"\ntie-share={PanelState.Prefs.TieShare * 100:0.#}% kept-answers={RunService.KeptAnswerCount} longest-search={PanelState.Prefs.SearchMinutes}"
        );

    private void Set(double share)
    {
        // Whole half steps, so repeated presses never drift.
        PanelState.Prefs.TieShare = Math.Round(share / Step) * Step;
        PanelState.Save();
        Show();
    }

    private void Clear()
    {
        RunService.ClearKeptAnswers();
        Show();
    }
}
