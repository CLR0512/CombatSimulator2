using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Results;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

internal sealed partial class SettingsView
{
    // The window: the header and help buttons, the text size and the skin.
    private void WindowSection(VBoxContainer column, Preferences prefs)
    {
        var header = SettingRow.Section(column, Tr.Get("Window"));
        LanguageRow.Add(header);
        _header = Cs2Check.Make(
            "Cs2SetHeader",
            "",
            prefs.ShowHeaderButton,
            on =>
            {
                PanelState.Prefs.ShowHeaderButton = on;
                PanelState.Save();
                HeaderChanged?.Invoke(on);
            }
        );
        SettingRow.Setting(
            header,
            Tr.Get("Show the Sim button in the header"),
            Tr.Get("Next to the game's Guide button. The pause menu keeps its entry."),
            _header
        );
        SettingRow.Setting(
            header,
            Tr.Get("Notices"),
            Tr.Get("Where refused changes show."),
            SettingRow.Options(
                "Cs2SetNotices",
                [Tr.Get("Top"), Tr.Get("Bottom")],
                prefs.NoticesAtBottom ? 1 : 0,
                i =>
                {
                    PanelState.Prefs.NoticesAtBottom = i == 1;
                    PanelState.Save();
                }
            )
        );
        SettingRow.Setting(
            header,
            Tr.Get("Hide game notifications"),
            Tr.Get("While this window is open: items found, level-ups and quests. They show again when it closes."),
            Cs2Check.Make(
                "Cs2SetQuiet",
                "",
                prefs.QuietNotifications,
                on =>
                {
                    PanelState.Prefs.QuietNotifications = on;
                    PanelState.Save();
                    Game.QuietNotifications.Follow(header, on && Cs2Modal.IsOpen);
                }
            )
        );
        _spoilers = Cs2Check.Make("Cs2SetSpoilers", "", prefs.SpoilerGuard, SpoilerChoice.Set);
        SettingRow.Setting(
            header,
            Tr.Get("Spoiler guard"),
            Tr.Get("Monsters you haven't killed and places you can't enter yet show as ???. Results stay the same."),
            _spoilers
        );
        _gearGuide = Cs2Check.Make(
            "Cs2SetGearGuide",
            "",
            prefs.ShowGearGuide,
            on =>
            {
                PanelState.Prefs.ShowGearGuide = on;
                PanelState.Save();
            }
        );
        SettingRow.Setting(
            header,
            Tr.Get("Show the gear search guide button"),
            Tr.Get("The (i) on the Gear tab that explains how gear search works."),
            _gearGuide
        );
        _scale = new ScaleRow(header);
        _motion = MotionRow.Add(header);
        _skin = SettingRow.Options("Cs2SetSkin", [], 0, SkinChosen);
        var skinHelp = SettingRow.Setting(
            header,
            Tr.Get("Skin"),
            Tr.Get("The game's look or mElvUI's flat one. Either works with mElvUI on or off."),
            _skin
        );
        _skinNote = Ui.Label("", TextStyle.Tiny, "Cs2SetSkinNote", wrap: true);
        _skinNote.LabelSettings = SkinNoteStyle();
        skinHelp.GetParent().AddChild(_skinNote);
        ShowSkin();
    }

    // How runs simulate: trials, where, fresh, stopping early, copies and the seed, as the ☰ menu shows them too.
    private void SimulationSection(VBoxContainer column) =>
        _runSettings = new RunSettings(SettingRow.Section(column, Tr.Get("Simulation")), "Cs2Set", help: true);

    private TieShareRow _tie = null!;

    // The results' defaults: what to rank by and the time unit; then the gear search's margin and kept answers.
    private void ResultsSection(VBoxContainer column, Preferences prefs)
    {
        var results = SettingRow.Section(column, Tr.Get("Results"));
        var metrics = DefaultMetrics();
        var rankBy = SettingRow.Options(
            "Cs2SetRankBy",
            metrics.Select(m => m.Key == Metrics.MainXp ? Words.Metric(m) : Words.MetricName(m, null)),
            Math.Max(0, metrics.FindIndex(m => m.Key == prefs.MetricDefault)),
            i =>
            {
                PanelState.Prefs.MetricDefault = metrics[i].Key;
                PanelState.Save();
            }
        );
        SettingRow.Setting(results, Tr.Get("Rank by, when CS2 opens"), "", rankBy);
        var unit = SettingRow.Options(
            "Cs2SetUnit",
            ResultsToolbar.Units.Select(Words.PerUnit),
            Array.IndexOf(ResultsToolbar.Units, prefs.Unit),
            i =>
            {
                PanelState.Prefs.TimeUnit = TimeUnits.Name(ResultsToolbar.Units[i]);
                PanelState.Update();
            }
        );
        SettingRow.Setting(results, Tr.Get("Time unit"), "", unit);
        _tie = new TieShareRow(results);
    }

    // Saved runs: how many to keep, clearing them, and the list.
    private void SavedRunsSection(VBoxContainer column, Preferences prefs)
    {
        var saved = SettingRow.Section(column, Tr.Get("Saved runs"));
        _savedTitle = column.GetChild<Control>(column.GetChildCount() - 2); // the section's ribbon
        var keep = SettingRow.Options(
            "Cs2SetKeep",
            KeepChoices.Select(k => Tr.Plural("{0} run", "{0} runs", k, k)),
            Index(KeepChoices, prefs.KeepRuns),
            i =>
            {
                PanelState.Prefs.KeepRuns = KeepChoices[i];
                RunStore.Shared.Max = PanelState.Prefs.KeepRuns;
                PanelState.Save();
            }
        );
        _clear = new ClearRuns();
        _clear.Cleared += () =>
        {
            RefreshSize();
            _history.Refresh();
        };
        keep.ItemSelected += _ => _history?.Refresh();
        var keepRow = Ui.Row(10);
        keepRow.AddChild(keep.Root);
        keepRow.AddChild(_clear.Button);
        keepRow.AddChild(_clear.Line);
        _size = SettingRow.Setting(saved, Tr.Get("Keep the last"), "", keepRow);
        _history = new HistoryList();
        _history.Shown += () => HistoryShown?.Invoke();
        _history.CompareChosen += id => HistoryCompare?.Invoke(id);
        saved.AddChild(_history.Root);
    }
}
