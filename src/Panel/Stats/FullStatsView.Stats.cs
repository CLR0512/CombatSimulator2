using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Stats;

// The stats: each group's stats for one side, or for A, B and B − A with the differing count in the header.
internal sealed partial class FullStatsView
{
    private static readonly (StatGroup Group, Func<string> Name, bool Left)[] StatGroups =
    [
        (StatGroup.Levels, () => Tr.Get("Levels"), true),
        (StatGroup.Offence, () => Tr.Get("Offence"), true),
        (StatGroup.Defence, () => Tr.Get("Defence"), true),
        (StatGroup.Other, () => Tr.Get("Other"), false),
        (StatGroup.Totals, () => Tr.Get("Equipment stats"), false),
    ];

    private VBoxContainer _left = null!;
    private VBoxContainer _right = null!;
    private Label _statsCount = null!;
    private int _statRows;
    private int _differing;

    private PanelContainer BuildStats()
    {
        var stats = Look.Panel(Art.SideBox, "Cs2StatsPanel");
        stats.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var column = Ui.Add(stats, Ui.Column(8));
        var head = Ui.Add(column, Ui.Row(10));
        head.AddChild(Ui.Label(Tr.Get("Stats"), TextStyle.Heading));
        _statsCount = Ui.Label("", TextStyle.Tiny, "Cs2StatsCount");
        _statsCount.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_statsCount);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);
        var columns = Ui.Add(scroll, Ui.Row(24, "Cs2Stats"));
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _left = Ui.Add(columns, Ui.Column(2));
        _left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _right = Ui.Add(columns, Ui.Column(2));
        _right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return stats;
    }

    /// <summary>The stats of <paramref name="sheets"/> side by side: one side's, or A's and B's by key.</summary>
    public static IReadOnlyList<StatDiff> StatsOf(IReadOnlyList<SetupSheet> sheets) =>
        sheets.Count > 1
            ? SheetDiff.Of(sheets[0], sheets[1]).Stats
            : [.. sheets[0].Stats.Select(s => new StatDiff(s.Key, s, null, null))];

    /// <summary>
    /// The stat's name; both sides' when they differ, as accuracy does between combat styles ("Accuracy Rating
    /// (Slash) / Accuracy Rating (Accurate)"), so B's value is never read as A's kind.
    /// </summary>
    public static string Name(StatDiff d)
    {
        var a = d.A != null ? Tr.Note(d.A.Name) : null;
        var b = d.B != null ? Tr.Note(d.B.Name) : null;
        return a != null && b != null && a != b ? a + " / " + b : (a ?? b)!;
    }

    /// <summary>B − A as the stat is written ("+1.3s" for an interval held in milliseconds), else from the numbers.</summary>
    public static string Delta(StatDiff d) =>
        d.A != null && d.B != null && SheetDiff.TextDelta(d.A.Text, d.B.Text) is { } written ? written
        : d.Delta is { } v && v != 0 ? (v > 0 ? "+" : "") + Ui.Number(v)
        : "";

    private void DrawStats()
    {
        Ui.Clear(_left);
        Ui.Clear(_right);
        var two = _sheets.Count > 1;
        var diffs = StatsOf(_sheets);
        _statRows = diffs.Count;
        _differing = two ? diffs.Count(d => d.Differs) : 0;
        _statsCount.Text = two
            ? Tr.Format("differing {0} of {1}", [_differing, diffs.Count])
            : Tr.Plural("{0} stat", "{0} stats", diffs.Count, diffs.Count);
        var only = two && PanelState.Prefs.StatsOnlyDifferences;
        foreach (var (group, name, left) in StatGroups)
        {
            var rows = diffs.Where(d => (d.A ?? d.B)!.Group == group && (!only || d.Differs)).ToList();
            if (rows.Count == 0)
                continue;
            var column = left ? _left : _right;
            column.AddChild(Caption(name(), two));
            foreach (var d in rows)
                column.AddChild(StatRow(d, two));
        }
    }

    // "─── Levels ───", with the A, B and B − A headings over a two-sided list.
    private static HBoxContainer Caption(string name, bool two)
    {
        var row = Ui.Row(6);
        row.AddChild(new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(Ui.Label(name, TextStyle.Label));
        row.AddChild(new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        if (two)
            foreach (var heading in new[] { SideLine.A, SideLine.B, Tr.Format("{0} − {1}", SideLine.B, SideLine.A) })
                row.AddChild(SideLine.Cell(heading, TextStyle.Tiny, 70));
        return row;
    }

    // A neutral row: only B − A is coloured, green where B is better and red where it is worse.
    private static HBoxContainer StatRow(StatDiff d, bool two)
    {
        var line = (d.A ?? d.B)!;
        var row = Ui.Row(6, Ui.Id("Cs2Stat", d.Key));
        var name = Ui.Clipped("", TextStyle.Small);
        Ui.SetClipped(name, Name(d));
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);
        if (!two)
        {
            row.AddChild(Cell(line.Text, TextStyle.Small, null, 120));
            return row;
        }
        row.AddChild(Cell(d.A?.Text ?? "—", TextStyle.Small, null));
        row.AddChild(Cell(d.B?.Text ?? "—", TextStyle.Small, null));
        row.AddChild(
            Cell(
                Delta(d),
                TextStyle.Small,
                d.BBetter switch
                {
                    true => Look.Safe,
                    false => Look.Deadly,
                    null => null,
                }
            )
        );
        return row;
    }

    private static Label Cell(string text, TextStyle style, Color? colour, float width = 70)
    {
        var label = Ui.Label(text, style);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        if (colour is { } c)
            label.LabelSettings = Compare.CompareText.Coloured(c, style);
        return label;
    }
}
