using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>Settings' "Bug reports": CS2's version, saving a report, and what reports keep and carry.</summary>
internal static class ReportSection
{
    private static readonly int[] CrashSpaceChoices = [5, 10, 15, 20, 30, 50];

    // The version, to quote in a report, and the button that saves one.
    public static void Add(VBoxContainer column)
    {
        var reports = SettingRow.Section(column, Tr.Get("Bug reports"));
        SettingRow.Setting(
            reports,
            Tr.Format("Combat Simulator 2 {0}", RunService.Cs2Version),
            Tr.Get(
                "Zips CS2's logs, settings and recent runs into mods/CombatSimulator2/reports, to send with a bug report."
            ),
            Ui.Button(Tr.Get("Save bug report"), Look.Button8, "Cs2SetReport", Report.ReportNote.Save)
        );
        var space = SettingRow.Options(
            "Cs2SetCrashSpace",
            CrashSpaceChoices.Select(mb => Tr.Format("{0} MB each", mb)),
            Math.Max(0, Array.IndexOf(CrashSpaceChoices, PanelState.Prefs.CrashFilesMegabytes)),
            i =>
            {
                PanelState.Prefs.CrashFilesMegabytes = CrashSpaceChoices[i];
                RunService.CrashFilesBytes = CrashSpaceChoices[i] * 1024L * 1024;
                PanelState.Save();
            }
        );
        SettingRow.Setting(
            reports,
            Tr.Get("Allocated space"),
            Tr.Get("Most each crash dump or report takes in CombatSimulator2's folder. Oldest go first."),
            space
        );
        SettingRow.Setting(
            reports,
            Tr.Get("Include my character's save"),
            Tr.Get(
                "Lets the developer rerun your runs. Names your character, not your Windows or Steam account. Public if posted on GitHub or Discord."
            ),
            Cs2Check.Make(
                "Cs2SetReportSave",
                "",
                PanelState.Prefs.ReportIncludesSave,
                on =>
                {
                    PanelState.Prefs.ReportIncludesSave = on;
                    PanelState.Save();
                }
            )
        );
    }
}
