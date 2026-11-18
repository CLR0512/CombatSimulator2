using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The kept comparisons as rows, for the Compare picker and for Settings: each named by its two sides, with its
/// target count, date and character. Show opens one as it finished, starting no run and leaving the sides being set
/// up as they are. One whose run is gone is listed without Show.
/// </summary>
internal static class RecentComparisons
{
    /// <summary>What the rows would show now: equal stamps, equal rows.</summary>
    public static string Stamp() =>
        string.Join(
            '|',
            RunService.Instance.KeptComparisons.Select(e =>
                $"{e.Record.Id}{(e.GoneRun != null ? "!" : "")}{(e.AnotherBuild(LiveGame.GameBuild) ? "~" : "")}"
            )
        );

    /// <summary>
    /// Fills <paramref name="into"/> with one row per kept comparison, newest first, named
    /// <paramref name="prefix"/> and its index. Returns how many there are.
    /// </summary>
    /// <param name="shown">Runs after a comparison was opened.</param>
    /// <param name="removed">With it, each row has Remove, and this runs after one was removed.</param>
    /// <param name="said">Takes the reason when opening one is refused.</param>
    public static int Fill(Container into, string prefix, Action shown, Action? removed, Action<string> said)
    {
        Ui.Clear(into);
        var kept = RunService.Instance.KeptComparisons;
        for (var i = 0; i < kept.Count; i++)
            into.AddChild(Row(kept[i], prefix + i.ToString(CultureInfo.InvariantCulture), shown, removed, said));
        return kept.Count;
    }

    private static PanelContainer Row(
        KeptComparison entry,
        string name,
        Action shown,
        Action? removed,
        Action<string> said
    )
    {
        var record = entry.Record;
        var root = Look.Panel(Art.RowBox, name);
        var line = Ui.Add(root, Ui.Row(10));
        var text = Ui.Add(line, Ui.Column(0));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // The side letters in gold, as the cards and the comparison's columns draw them.
        text.AddChild(
            SideLine.Make(
                Tr.Format(
                    "{0} {1} · {2} {3}",
                    [
                        SideLine.A,
                        Side(entry.A, record.Kind == ComparisonKind.NowAgainstSaved),
                        SideLine.B,
                        Side(entry.B, false),
                    ]
                ),
                TextStyle.Body
            )
        );
        var under = Ui.Add(text, Ui.Row(8));
        var when = record.CreatedAt.ToLocalTime().ToString("g", Tr.Culture);
        under.AddChild(
            Ui.Label(
                Tr.Plural(
                    "{0} target · {1} · {2}",
                    "{0} targets · {1} · {2}",
                    record.Targets.Count,
                    Ui.Number(record.Targets.Count),
                    when,
                    entry.Character ?? "–"
                ),
                TextStyle.Tiny
            )
        );
        if (entry.GoneRun != null)
            under.AddChild(
                Tag(Tr.Get("run gone"), Tr.Get("One of its saved runs is no longer there, so it can't be shown."))
            );
        else if (entry.AnotherBuild(LiveGame.GameBuild))
            under.AddChild(Tag(Tr.Get("another game build"), Tr.Get("Simulated on another game build than this one.")));
        if (entry.Unchecked)
            under.AddChild(
                Tag(
                    // Translators: A saved run made on a game version CS2 wasn't checked with.
                    Tr.Get("not checked"),
                    Tr.Get("Made on a game version CS2 wasn't checked with. Results may be wrong.")
                )
            );
        if (entry.GoneRun == null)
        {
            var show = Ui.Button(
                Tr.Get("Show", "comparison"),
                Look.Button4,
                name + "Show",
                () =>
                {
                    try
                    {
                        RunService.Instance.ShowKept(record.Id);
                        shown();
                    }
                    catch (CommandException e)
                    {
                        said(e.Message);
                    }
                }
            );
            show.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            line.AddChild(show);
        }
        if (removed != null)
        {
            var remove = Ui.Button(
                Tr.Get("Remove", "comparison"),
                Look.Button4,
                name + "Remove",
                () =>
                {
                    RunService.Instance.RemoveKept(record.Id);
                    removed();
                }
            );
            remove.TooltipText = Tr.Get("Removes the comparison. Its two runs stay.");
            remove.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            line.AddChild(remove);
        }
        return root;
    }

    // A side as its run's header names it: the setup, and how many changes it ran with.
    private static string Side(RunHeader? run, bool saved)
    {
        if (run == null)
            return "–";
        if (saved)
            return run.Name ?? Tr.Get("saved run");
        var name = run.Setup == Setup.Current ? Tr.Get("Character as is") : run.Setup;
        // Counted, not read: a run saved by a later CS2 can hold a kind of change this one doesn't know.
        return run.Changes is { Length: > 0 } changes
            ? Tr.Note(ChangeNotes.Label(name, changes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length))
            : name;
    }

    private static Label Tag(string text, string why)
    {
        var tag = Ui.Label(text, TextStyle.Tiny);
        tag.Modulate = Look.Risky;
        tag.TooltipText = why;
        tag.MouseFilter = Control.MouseFilterEnum.Stop;
        return tag;
    }
}
