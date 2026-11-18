using System.Text;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary><c>cs2 report</c>: saves a bug report and lists what is in it.</summary>
internal static class Cs2ReportCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.Add(
            new(
                "report",
                "cs2 report [no-save]",
                [],
                ["no-save"],
                Report,
                NeedsGame: false,
                Purpose: "Save a bug report zip in mods/CombatSimulator2/reports and list its files; no-save leaves the character's save out"
            )
        );

    private static void Report(CommandCall call, CommandArgs args) =>
        MainThreadReply.ReplyWhen<ReportResult>(
            call,
            done =>
                RunService.Instance.SaveReport(
                    Panel.PanelState.Prefs.ReportIncludesSave && !args.Flag("no-save"),
                    (result, error) => done(result, error == null ? null : new IOException(error))
                ),
            (result, error) =>
            {
                if (result == null)
                {
                    call.Error($"couldn't save the report: {error?.Message}");
                    return;
                }
                var text = new StringBuilder($"saved {result.File} ({result.Bytes / 1024} KB)");
                foreach (var entry in result.Entries)
                    text.Append("\n  ").Append(entry);
                call.Ok(text.ToString());
            },
            TimeSpan.FromSeconds(120),
            "couldn't save the report: it took over 2 minutes"
        );
}
