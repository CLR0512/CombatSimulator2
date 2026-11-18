using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>
/// A comparison as a chart: per target a pair of bars, A outlined and B solid (green better, red worse, plain with no
/// clear difference), each with its 95% range as a whisker and the difference above, in the comparison's order rather
/// than by area: its question is where B helps most. Scrolls sideways when the targets don't fit; hovering a pair shows
/// both values and the difference; clicking a portrait ignores its target, or returns it, as on the run's chart.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class CompareChart
{
    private const float Step = 78;
    private const float BarWidth = 22;
    private const float Left = 64;
    private const float Top = 34;

    // Room under the baseline for the portrait and the name, and nothing more: the scrollbar sits right under them.
    private const float Bottom = 76;

    private readonly VBoxContainer _root;
    private readonly ScrollContainer _scroll;
    private readonly Control _plot;
    private readonly Control _grid;
    private readonly SideLine _legendA = new(TextStyle.Small);
    private readonly SideLine _legendB = new(TextStyle.Small);
    private readonly HBoxContainer _skillsLegend;
    private List<Difference> _pairs = [];
    private int _hovered = -1;

    public CompareChart()
    {
        _root = Ui.Column(6, "Cs2CompareChart");
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _scroll = new ScrollContainer
        {
            Name = "Cs2CompareChartScroll",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        Flat(_scroll.GetHScrollBar());
        // The axis stays put beside the pairs, so only they scroll and the scrollbar spans only them.
        // Short of the panel's right edge, so the scrollbar ends inside its border as the legend starts inside it.
        var plotRow = Ui.Row(0, "Cs2CompareChartRow");
        // The axis and its lines are drawn behind the pairs, outside their fade, so a line runs on from its tick.
        _grid = Ui.Add(_root, Ui.Margin(plotRow, 0, 0, 10, 0));
        _grid.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _grid.Draw += PaintAxis;
        _scroll.GetHScrollBar().ValueChanged += _ => _grid.QueueRedraw();
        Ui.Add(
            plotRow,
            new Control
            {
                Name = "Cs2CompareChartAxis",
                CustomMinimumSize = new Vector2(Left, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }
        );
        plotRow.AddChild(ScrollFade.Sideways(_scroll, edge: 16));
        _plot = new Control
        {
            Name = "Cs2CompareChartPlot",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _plot.Draw += Paint;
        _plot.Resized += _grid.QueueRedraw;
        _plot.GuiInput += Input;
        _plot.SetMeta(InstantTooltips.Live, true);
        _plot.MouseExited += () =>
        {
            _hovered = -1;
            _plot.TooltipText = "";
            _plot.QueueRedraw();
        };
        _scroll.AddChild(_plot);
        // Clear of the panel's bottom edge, which its box draws inside its content margin.
        var legend = Ui.Row(18, "Cs2CompareLegend");
        _root.AddChild(Ui.Margin(legend, 8, 0, 0, 10));
        legend.AddChild(Swatch(outline: true, Look.BarFill));
        legend.AddChild(_legendA.Root);
        legend.AddChild(Swatch(outline: false, Look.Safe));
        legend.AddChild(_legendB.Root);
        legend.AddChild(Swatch(outline: false, Look.Deadly));
        legend.AddChild(SideLine.Make(Tr.Format("{0} worse", SideLine.B), TextStyle.Small));
        var whisker = new Control
        {
            CustomMinimumSize = new Vector2(10, 14),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        whisker.Draw += () => ChartMarks.Whisker(whisker, new Rect2(Vector2.Zero, whisker.Size), Colors.White, 2);
        var range = Ui.Row(6);
        range.AddChild(whisker);
        range.AddChild(Ui.Label(Tr.Get("95% range"), TextStyle.Tiny));
        legend.AddChild(range);
        // Only while a pair carries the mark: its bars are left blue, as neither skill's XP is better than the other's.
        _skillsLegend = Ui.Add(legend, Ui.Row(6));
        _skillsLegend.AddChild(Ui.Label("≠", TextStyle.Body));
        _skillsLegend.AddChild(Ui.Label(Tr.Get("different skills"), TextStyle.Tiny));
    }

    public Control Root => _root;

    public int Pairs => _pairs.Count;

    // A thin, square-cornered bar in the details tabs' grey-blue: the theme's rounded one stands out on the chart.
    private static void Flat(ScrollBar bar)
    {
        var grey = Color.FromHtml("#8a9bb1");
        StyleBoxFlat Box(Color colour)
        {
            var box = new StyleBoxFlat { BgColor = colour };
            box.ContentMarginTop = box.ContentMarginBottom = 3;
            return box;
        }
        bar.AddThemeStyleboxOverride("scroll", Box(Colors.White with { A = 0.06f }));
        bar.AddThemeStyleboxOverride("scroll_focus", Box(Colors.White with { A = 0.06f }));
        bar.AddThemeStyleboxOverride("grabber", Box(grey with { A = 0.55f }));
        bar.AddThemeStyleboxOverride("grabber_highlight", Box(grey with { A = 0.8f }));
        bar.AddThemeStyleboxOverride("grabber_pressed", Box(grey));
    }

    private static Control Swatch(bool outline, Color colour)
    {
        var swatch = new Control
        {
            CustomMinimumSize = new Vector2(14, 14),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        swatch.Draw += () =>
        {
            var rect = new Rect2(Vector2.Zero, swatch.Size);
            if (outline)
                swatch.PxRect(rect, colour, false, 2);
            else
                swatch.PxRect(rect, colour);
        };
        return swatch;
    }

    public void Show(ComparedRuns comparison, CompareTable table)
    {
        var saved = comparison.Record.Kind == ComparisonKind.NowAgainstSaved;
        _legendA.Show(CompareText.Side(SideLine.A, comparison.A, saved));
        _legendB.Show(Tr.Format("{0}, better", CompareText.Side(SideLine.B, comparison.B, saved: false)));
        _pairs = [.. table.Rows.Select(r => r.Difference).Where(d => d.A != null || d.B != null)];
        _skillsLegend.Visible = _pairs.Any(d => d.SkillsDiffer);
        _plot.CustomMinimumSize = new Vector2(_pairs.Count * Step + 16, 0);
        _plot.QueueRedraw();
        _grid.QueueRedraw();
    }

    public void HideHover()
    {
        _hovered = -1;
        _plot.TooltipText = "";
    }

    private void Input(InputEvent e)
    {
        if (e is InputEventMouseMotion motion)
        {
            var index = (int)Math.Floor(motion.Position.X / Step);
            index = index >= 0 && index < _pairs.Count ? index : -1;
            // Set on every move: the panel's tooltips read it anew each time the pointer moves.
            _plot.TooltipText = index < 0 ? "" : Hover(_pairs[index]);
            if (index == _hovered)
                return;
            _hovered = index;
            _plot.QueueRedraw();
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click && _hovered >= 0)
        {
            var key = _pairs[_hovered].Key;
            if (!PortraitAt(_hovered).HasPoint(click.Position))
                PanelState.Select(key);
            else if (!Engine.RunService.Instance.IsActive)
                PanelState.SetIgnored([key], !PanelState.Ignored.Contains(key));
        }
        // A right-click on a portrait runs that target alone, as on the target strip.
        else if (
            e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } only
            && _hovered >= 0
            && PortraitAt(_hovered).HasPoint(only.Position)
            && !Engine.RunService.Instance.IsActive
        )
            PanelState.RunOnly([_pairs[_hovered].Key]);
    }

    private float Baseline => _plot.Size.Y - Bottom;

    private Rect2 PortraitAt(int index) => new(index * Step + 8 + BarWidth - 14, Baseline + 8, 34, 34);

    // The target's name and its area apart: the label under a pair has room for the name only.
    private static (string Name, string Area) NameAndArea(Difference d)
    {
        if (PanelState.Catalog?.Find(d.Key) is { } info)
            return (info.Name, info.AreaName);
        var open = d.Name.IndexOf(" (", StringComparison.Ordinal);
        return open > 0 && d.Name.EndsWith(')') ? (d.Name[..open], d.Name[(open + 2)..^1]) : (d.Name, "");
    }

    // "A: 1,000  ± 12": the side's value, its range in grey.
    private static string Side(string letter, Estimate? estimate) =>
        Tr.Format(
            "{0}: {1}",
            letter,
            estimate is { } e ? Ui.Number(e.Point) + "  " + Marks.Muted(CompareText.Range(e)) : "–"
        );

    // A line in one colour around the side letters it names: a mark can't sit inside another.
    private static string Tinted(string line, Func<string, string> tint)
    {
        var parts = line.Split(Marks.Side);
        return string.Concat(
            parts.Select((part, i) => i == 0 ? tint(part) : Marks.Side.ToString() + part[..1] + tint(part[1..]))
        );
    }

    private static string Hover(Difference d)
    {
        var (name, area) = NameAndArea(d);
        var lines = new List<string>
        {
            area.Length > 0 ? name + " · " + Marks.Muted(area) : name,
            Side(SideLine.A, d.A),
            Side(SideLine.B, d.B),
        };
        foreach (var (letter, stall) in new[] { (SideLine.A, d.StallA), (SideLine.B, d.StallB) })
            if (stall != null)
                lines.Add(
                    Tinted(
                        // Translators: Side {0} (A or B) can't kill the target in time; {1} says why, e.g. "Armour stops your hits".
                        Tr.Format(
                            "{0} can't kill it in time: {1}",
                            [letter, Tr.Note(StallReasons.Brief(stall.Reason))]
                        ),
                        Marks.Bad
                    )
                );
        if (d.Delta is { } delta)
        {
            var line = Tr.Format(
                "{4} − {5}: {0} ({1} to {2}) · {3}",
                [
                    CompareText.Signed(delta.Point),
                    CompareText.Signed(delta.Low),
                    CompareText.Signed(delta.High),
                    d.Reason is { } died ? Tr.Note(died)
                    // Translators: Comparison chart hover, in place of the verdict: the sides' main skills differ.
                    : d.SkillsDiffer ? Tr.Get("different skills, not compared")
                    : CompareText.Verdict(d.Verdict),
                    SideLine.B,
                    SideLine.A,
                ]
            );
            // The whole line takes the verdict's colour; its letters stay gold.
            lines.Add(
                d.Verdict == Verdict.Better ? Tinted(line, Marks.Good)
                : d.Verdict == Verdict.Worse ? Tinted(line, Marks.Bad)
                : line
            );
        }
        lines.Add(
            Marks.Muted(
                PanelState.Ignored.Contains(d.Key)
                    ? Tr.Get("Ignored in the next run · click the portrait to return it")
                    : Tr.Get("Click the portrait to ignore it in the next run · right-click to run only it")
            )
        );
        return string.Join("\n", lines);
    }

    // The axis's top: the highest whisker or value, with room above it for the difference.
    private double Max()
    {
        var max = 0.0;
        foreach (var d in _pairs)
        foreach (var e in new[] { d.A, d.B })
            if (e != null && double.IsFinite(e.High))
                max = Math.Max(max, e.High);
            else if (e != null && double.IsFinite(e.Point))
                max = Math.Max(max, e.Point);
        return (max <= 0 ? 1 : max) * 1.08;
    }

    // The values up the axis and their lines across the plot, from the plot's height, which the scrollbar shortens. A
    // line breaks where a bar stands over it: drawn behind the pairs, it would show through a bar the edges fade.
    private void PaintAxis()
    {
        var height = _plot.Size.Y - Top - Bottom;
        if (height <= 10 || _pairs.Count == 0)
            return;
        var max = Max();
        float Y(double v) => (float)(Top + height - Math.Clamp(v / max, 0, 1) * height);
        var muted = Look.Muted(_grid);
        var line = muted with { A = 0.18f };
        var offset = Left - _scroll.ScrollHorizontal;
        var end = _grid.Size.X - 10;
        for (var i = 0; i <= 4; i++)
        {
            var v = max / 1.08 * i / 4;
            var y = Y(v);
            var from = Left - 4f;
            // The baseline runs under the bars' feet, whole.
            for (var p = 0; p < _pairs.Count && i > 0; p++)
            {
                var x = offset + p * Step + 8;
                foreach (var (left, e) in new[] { (x, _pairs[p].A), (x + BarWidth + 6, _pairs[p].B) })
                {
                    if (e is not { } bar || !double.IsFinite(bar.Point) || Y(bar.Point) >= y || left + BarWidth <= from)
                        continue;
                    if (left >= end)
                        break;
                    if (left > from)
                        _grid.PxLine(new Vector2(from, y), new Vector2(left, y), line, 1);
                    from = left + BarWidth;
                }
            }
            if (from < end)
                _grid.PxLine(new Vector2(from, y), new Vector2(end, y), line, 1);
            _grid.DrawString(
                Look.Font(400),
                new Vector2(4, y + 4),
                Ui.Number(v),
                HorizontalAlignment.Right,
                Left - 12,
                12,
                muted
            );
        }
    }

    private void Paint()
    {
        var size = _plot.Size;
        var height = size.Y - Top - Bottom;
        if (height <= 10 || _pairs.Count == 0)
            return;
        var max = Max();
        float Y(double v) => (float)(Top + height - Math.Clamp(v / max, 0, 1) * height);
        var text = Look.Text(_plot);
        var muted = Look.Muted(_plot);
        var font = Look.Font(400);
        var game = LiveGame.Find();
        for (var i = 0; i < _pairs.Count; i++)
        {
            var d = _pairs[i];
            var x = i * Step + 8;
            var ignored = PanelState.Ignored.Contains(d.Key);
            if (i == _hovered)
                _plot.PxRect(new Rect2(x - 6, Top - 26, Step - 4, height + 30), text with { A = 0.06f });
            if (d.A is { } a && double.IsFinite(a.Point))
            {
                var rect = new Rect2(x, Y(a.Point), BarWidth, Y(0) - Y(a.Point));
                _plot.PxRect(rect, Colors.Black with { A = 0.25f });
                _plot.PxRect(rect, ignored ? muted : Look.BarFill, false, 2);
                Whisker(x + BarWidth / 2, a, Y, text);
            }
            else if (d.StallA != null)
                CantKill(x, Y(0), ignored ? muted : Look.Deadly);
            var bx = x + BarWidth + 6;
            if (d.B is { } b && double.IsFinite(b.Point))
            {
                var colour = ignored
                    ? muted with
                    {
                        A = 0.35f,
                    }
                    : d.Verdict switch
                    {
                        Verdict.Better => Look.Safe,
                        Verdict.Worse => Look.Deadly,
                        _ => Look.BarFill,
                    };
                _plot.PxRect(new Rect2(bx, Y(b.Point), BarWidth, Y(0) - Y(b.Point)), colour);
                Whisker(bx + BarWidth / 2, b, Y, text);
            }
            else if (d.StallB != null)
                CantKill(bx, Y(0), ignored ? muted : Look.Deadly);
            // The cross on the side that can't kill says it; a word above every such pair would crowd a long chart.
            var label =
                d.OnlyKills != null ? ""
                : d.SkillsDiffer ? "≠"
                : d.Percent != null ? CompareText.SignedPercent(d.Percent.Point)
                : d.Delta != null ? CompareText.Signed(d.Delta.Point)
                : "–";
            var top = Math.Min(d.A is { } ea ? Y(ea.High) : Y(0), d.B is { } eb ? Y(eb.High) : Y(0));
            _plot.DrawString(
                Look.Font(700),
                new Vector2(x - 6, top - 8),
                label,
                HorizontalAlignment.Center,
                Step - 4,
                13,
                DiffBar.Colour(d.Verdict, _plot)
            );
            var at = PortraitAt(i);
            if (game != null && Art.Target(game, TargetSpec.FromKey(d.Key)) is { } portrait)
                _plot.DrawTextureRect(portrait, at, false, ignored ? Colors.White with { A = 0.4f } : Colors.White);
            if (ignored)
            {
                var cross = at.Grow(-4);
                _plot.PxLine(cross.Position, cross.End, Look.Deadly, 2);
                _plot.PxLine(
                    new Vector2(cross.End.X, cross.Position.Y),
                    new Vector2(cross.Position.X, cross.End.Y),
                    Look.Deadly,
                    2
                );
            }
            _plot.DrawString(
                font,
                new Vector2(x - 6, Y(0) + 58),
                Fit(NameAndArea(d).Name, font, 11, Step - 4),
                HorizontalAlignment.Center,
                Step - 4,
                11,
                text
            );
        }
    }

    // The text as it fits the width, cut with an ellipsis when it doesn't.
    private static string Fit(string text, Font font, int size, float width)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= width)
            return text;
        var cut = text;
        while (cut.Length > 1 && font.GetStringSize(cut + "…", HorizontalAlignment.Left, -1, size).X > width)
            cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }

    // A cross on the baseline where a side's bar would stand: its fights all ran over, so it has no rate to draw.
    private void CantKill(float x, float baseline, Color colour)
    {
        const float size = 12;
        var left = x + (BarWidth - size) / 2;
        var top = baseline - size - 3;
        _plot.PxLine(new Vector2(left, top), new Vector2(left + size, top + size), colour, 2);
        _plot.PxLine(new Vector2(left + size, top), new Vector2(left, top + size), colour, 2);
    }

    private void Whisker(float x, Estimate e, Func<double, float> y, Color colour)
    {
        if (!double.IsFinite(e.Low) || !double.IsFinite(e.High))
            return;
        var top = y(e.High);
        var bottom = y(e.Low);
        _plot.PxLine(new Vector2(x, top), new Vector2(x, bottom), colour, 2);
        _plot.PxLine(new Vector2(x - 4, top), new Vector2(x + 4, top), colour, 2);
        _plot.PxLine(new Vector2(x - 4, bottom), new Vector2(x + 4, bottom), colour, 2);
    }
}
