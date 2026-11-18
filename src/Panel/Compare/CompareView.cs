using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Results;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>
/// The pinned targets side by side: a column per pin and a row per metric, the best of each row in gold and the
/// others with how far behind they are; the last row is each pin's top loot. Replaces the results while open.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class CompareView
{
    private const float LabelWidth = 150;

    private readonly VBoxContainer _root;
    private readonly MarginContainer _frame;
    private readonly Label _title;
    private readonly GridContainer _grid;

    public CompareView(Action back)
    {
        _root = Ui.Column(10, "Cs2CompareView");
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var head = Ui.Add(_root, Ui.Row(14, "Head"));
        head.AddChild(Ui.Button(Tr.Get("◂ Back to results"), Look.Button8, "Cs2CompareBack", back));
        _title = Ui.Label("", TextStyle.Heading);
        head.AddChild(_title);
        head.AddChild(Ui.Spacer());
        head.AddChild(
            Ui.Label(
                Tr.Get("Same setup, build and seed · bars relative to the best of the pins · gold = best"),
                TextStyle.Tiny
            )
        );
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _root.AddChild(scroll);
        _grid = new GridContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_grid);
        _frame = Ui.Margin(_root, 0, 0, 14, 0);
        _frame.Name = "Cs2CompareFrame";
        _frame.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _frame.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    }

    // In from the window's right edge, whose frame the last column's bars would otherwise run under.
    public Control Root => _frame;

    public int Rows { get; private set; }

    public void Show(Run run, IReadOnlyList<string> pins, TimeUnit unit)
    {
        Ui.Clear(_grid);
        _grid.Columns = pins.Count + 1;
        _title.Text = Tr.Plural("Comparing {0} pinned target", "Comparing {0} pinned targets", pins.Count, pins.Count);
        var game = LiveGame.Find();
        var names = PanelState.Names();

        var rows = Ranking.Compare(run, pins, unit, PanelState.Prices(), names);
        _grid.AddChild(Ui.Spacer(false, LabelWidth));
        // A pin with no values says why once, under its name; its cells are left empty.
        for (var i = 0; i < pins.Count; i++)
            _grid.AddChild(
                Header(run, pins[i], rows.Count > 0 && rows[0].Cells[i] is { Estimate: null } c ? c.Reasons : null)
            );

        // Rows the player pinned come first, in the order pinned; the rest keep theirs.
        var pinned = PanelState.Prefs.PinnedRows;
        var ordered = rows.OrderBy(r => pinned.IndexOf(r.Metric.Key) is var at and >= 0 ? at : int.MaxValue);
        foreach (var row in ordered)
        {
            // Items, currencies and rare events go in the top-loot row; zero everywhere says nothing.
            if (
                row.Metric.Key.Split(':')[0] is "item" or "currency" or "rare"
                || row.Cells.All(c => c.Estimate is not { Point: not 0 })
            )
                continue;
            var head = Ui.Row(4);
            head.CustomMinimumSize = new Vector2(LabelWidth, 0);
            head.AddChild(RowPin(row.Metric.Key, pinned.Contains(row.Metric.Key)));
            var label = Ui.Clipped(
                Words.MetricName(row.Metric, unit),
                TextStyle.Label,
                Ui.Id("Cs2CompareRow", row.Metric.Key)
            );
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(label);
            _grid.AddChild(head);
            foreach (var cell in row.Cells)
                _grid.AddChild(Cell(row, cell));
        }
        Rows = rows.Count;

        _grid.AddChild(Ui.Label(TopLoot(unit), TextStyle.Label));
        foreach (var key in pins)
        {
            var loot = Ui.Row(6);
            if (game != null)
                foreach (var (lootKey, value) in Loot.Top(run.Results.GetValueOrDefault(key), unit, 4))
                    loot.AddChild(
                        Slot.WithCount(
                            Art.Metric(game, lootKey),
                            Ui.Number(value),
                            40,
                            Words.Metric(Metrics.Describe(lootKey, names))
                        )
                    );
            _grid.AddChild(loot);
        }
    }

    // The pin before a row's name: gold while the row is pinned to the top, faint until then.
    private static Button RowPin(string metric, bool pinned)
    {
        var pin = Ui.Button("", "", Ui.Id("Cs2CompareRowPin", metric), () => PanelState.TogglePinnedRow(metric));
        pin.Flat = true;
        pin.Icon = Art.File(Art.PinPath);
        pin.ExpandIcon = true;
        pin.CustomMinimumSize = new Vector2(20, 20);
        pin.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        pin.TooltipText = pinned ? Tr.Get("Unpin row") : Tr.Get("Pin row to top");
        pin.Modulate = pinned ? Look.Best : Colors.White with { A = 0.3f };
        return pin;
    }

    // "Top loot / hour": the loot row's label in the unit.
    private static string TopLoot(TimeUnit unit) =>
        unit switch
        {
            TimeUnit.Kill => Tr.Get("Top loot / kill"),
            TimeUnit.Second => Tr.Get("Top loot / second"),
            TimeUnit.Minute => Tr.Get("Top loot / minute"),
            TimeUnit.Hour => Tr.Get("Top loot / hour"),
            _ => Tr.Get("Top loot / day"),
        };

    private static PanelContainer Header(Run run, string key, IReadOnlyList<Note>? reasons)
    {
        var game = LiveGame.Find();
        var panel = Look.Panel(Art.RowBox, Ui.Id("Cs2CompareCol", key));
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var row = Ui.Add(panel, Ui.Row(10));
        row.AddChild(Slot.Make(game != null ? Art.Target(game, TargetSpec.FromKey(key)) : Art.Missing, 48));
        var result = run.Results.GetValueOrDefault(key);
        var rank = new RankRow(key, result?.Name ?? key, null, null, 0, result?.State ?? TargetState.Queued, null);
        var (name, area) = RankList.Names(rank, PanelState.Catalog?.Find(key));
        var names = Ui.Add(row, Ui.Column(0));
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var nameRow = Ui.Add(names, Ui.Row(6));
        var nameLabel = Ui.Add(nameRow, Ui.Clipped(name, TextStyle.BodyStrong));
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // The same mark as the list's rows: a pin's risk shows where its values are compared.
        var died = result?.NoneSurvived == true;
        var mark = Ui.Add(nameRow, RiskMark.Make(Ui.Id("Cs2SideRisk", key)));
        RiskMark.Set(mark, result == null ? null : Risk.Of(result), died);
        names.AddChild(Ui.Clipped(area, TextStyle.Small));
        if (died)
            names.AddChild(DiedMark.Make(Ui.Id("Cs2SideDied", key)));
        if (RankList.Reason(reasons) is { } reason)
            names.AddChild(Ui.Label(reason, TextStyle.Small, Ui.Id("Cs2SideReason", key), wrap: true));
        var unpin = Ui.Button("", "", "Unpin", () => PanelPins.TogglePin(key));
        unpin.Flat = true;
        unpin.Icon = Art.File(Art.PinPath);
        unpin.ExpandIcon = true;
        unpin.CustomMinimumSize = new Vector2(26, 26);
        unpin.TooltipText = Tr.Get("Unpin");
        unpin.Modulate = Look.Best;
        row.AddChild(unpin);
        return panel;
    }

    private static VBoxContainer Cell(CompareRow row, CompareCell cell)
    {
        var column = Ui.Column(4);
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // The best value is written larger, which makes its cell taller: each cell's text and bar sit at its foot, the
        // text on one baseline, so the bars of a row line up.
        column.AddChild(
            new Control
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }
        );
        var line = Ui.Add(column, Ui.Row(6));
        if (cell.Estimate is { } e)
        {
            var value =
                row.Metric.Key == Metrics.DeathRate || FightNumbers.IsShare(row.Metric.Key) ? Ui.Percent(e.Point)
                : row.Metric.Key == Metrics.KillTime ? Words.Seconds(Ui.Number(e.Point))
                : Ui.Number(e.Point);
            var shown = Ui.Add(line, Ui.Label(value, cell.IsBest ? TextStyle.ValueBest : TextStyle.BodyStrong));
            // A died pin's values are shown greyed and measured against nothing: they were earned while dying.
            if (cell.Died)
                shown.Modulate = Colors.White with { A = 0.5f };
            else if (cell.IsBest)
                line.AddChild(BestTag.Make("Cs2SideBest", small: true));
            else
                line.AddChild(
                    Ui.Label(Tr.Format("−{0}%", Math.Abs(cell.DeltaPercent).ToString("0", Tr.Culture)), TextStyle.Tiny)
                );
            foreach (var label in line.GetChildren().OfType<Label>())
                label.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            var bar = Bar.Make("", 7);
            Bar.Set(bar, cell.OfBest, cell.IsBest ? Look.Best : Look.BarFill);
            column.AddChild(bar);
        }
        else
        {
            line.AddChild(Ui.Label("–", TextStyle.Small));
        }
        return column;
    }
}
