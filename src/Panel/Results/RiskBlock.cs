using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// A bar on the player's max HP: filled to the lowest HP reached, with a red tick at the highest hit taken. Values
/// past the scale are drawn at its end.
/// </summary>
internal static class HpBar
{
    public static Control Make(string name, long? lowest, long highest, long scale)
    {
        var bar = new Control
        {
            Name = name,
            CustomMinimumSize = new Vector2(40, 8),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var max = Math.Max(1, scale);
        bar.Draw += () =>
        {
            var size = bar.Size;
            var track = new Rect2(Vector2.Zero, size);
            if (Look.Track(bar) is StyleBoxTexture or null)
                bar.PxRect(track, Colors.Black with { A = 0.55f });
            else
                Look.Track(bar).Draw(bar.GetCanvasItem(), track);
            if (lowest is { } low && low > 0)
                bar.PxRect(new Rect2(0, 0, Math.Max(2, size.X * Math.Min(1f, (float)low / max)), size.Y), Look.Safe);
            var x = Math.Clamp(size.X * Math.Min(1f, (float)highest / max), 1, size.X - 1);
            bar.PxRect(new Rect2(x - 1.5f, -2, 3, size.Y + 4), Look.Deadly);
        };
        bar.ThemeChanged += bar.QueueRedraw;
        return bar;
    }

    /// <summary>The HP bar's legend: "■ lowest HP ▮ highest hit · scale: max HP 600".</summary>
    public static HFlowContainer Legend(long scale)
    {
        var row = Flow("Cs2RiskLegend");
        row.AddChild(Swatch(Look.Safe, 8));
        row.AddChild(Ui.Label(Tr.Get("lowest HP"), TextStyle.Tiny));
        row.AddChild(Swatch(Look.Deadly, 3));
        row.AddChild(Ui.Label(Tr.Get("highest hit"), TextStyle.Tiny));
        row.AddChild(Ui.Label("·", TextStyle.Tiny));
        row.AddChild(Ui.Label(Tr.Format("scale: max HP {0}", Ui.Number(scale)), TextStyle.Tiny));
        return row;
    }

    /// <summary>A row that wraps onto more lines when the panel is narrow or the text large.</summary>
    public static HFlowContainer Flow(string name = "")
    {
        var flow = new HFlowContainer();
        if (name.Length > 0)
            flow.Name = name;
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 2);
        return flow;
    }

    private static ColorRect Swatch(Color colour, float width) =>
        new()
        {
            Color = colour,
            CustomMinimumSize = new Vector2(width, 10),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
}

/// <summary>
/// The risk block of the details, after the four fight metrics: highest hit, lowest HP (or every fight died), the
/// HP bar, close calls with their range (or "No fight survived", where there is no share to give), and the warning
/// line. Older runs say "Not recorded".
/// </summary>
internal static class RiskBlock
{
    public static VBoxContainer Single(TargetResult result)
    {
        var block = Ui.Column(6, "Cs2RiskBlock");
        var risk = Risk.Of(result);
        var head = Ui.Add(block, Ui.Row(8));
        head.AddChild(Ui.Label(Tr.Get("Risk"), TextStyle.Heading));
        if (risk is not { Recorded: true })
        {
            block.AddChild(NotRecorded(Tr.Get("Highest hit")));
            block.AddChild(NotRecorded(Tr.Get("Lowest HP")));
            block.AddChild(NotRecorded(Tr.Get("Close calls")));
            block.AddChild(Ui.Label(Tr.Get("This run was saved before CS2 kept risk."), TextStyle.Tiny, wrap: true));
            return block;
        }
        // No spacer before it: the label takes all the row's free width, so its text is whole at every width.
        var over = Ui.Clipped(
            Tr.Plural("worst of {0} trial", "worst of {0} trials", result.TrialsDone, Ui.Number(result.TrialsDone)),
            TextStyle.Tiny,
            "Cs2RiskOver"
        );
        over.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(over);
        var scale = Scale(result, risk);
        var values = Ui.Add(block, HpBar.Flow());
        values.AddChild(Ui.Label(Tr.Get("Highest hit"), TextStyle.Small));
        values.AddChild(Ui.Label(Ui.Number(risk.HighestHit), TextStyle.BodyStrong));
        values.AddChild(Ui.Spacer(false, 10));
        values.AddChild(Ui.Label(Tr.Get("Lowest HP"), TextStyle.Small));
        if (risk.LowestHp is { } lowest)
        {
            var low = Ui.Label(Ui.Number(lowest), TextStyle.BodyStrong);
            if (risk.Warning)
                low.Modulate = Look.Deadly;
            values.AddChild(low);
            values.AddChild(Ui.Label(Tr.Format("of {0}", Ui.Number(scale)), TextStyle.Tiny));
        }
        else
        {
            var died = Ui.Label(Tr.Get("Died every fight"), TextStyle.BodyStrong);
            died.Modulate = Look.Deadly;
            values.AddChild(died);
        }
        block.AddChild(HpBar.Make("Cs2RiskBar", risk.LowestHp, risk.HighestHit, scale));
        block.AddChild(HpBar.Legend(scale));
        var calls = Ui.Add(block, Ui.Row(6, "Cs2CloseCalls"));
        calls.MouseFilter = Control.MouseFilterEnum.Stop;
        calls.TooltipText = CloseCallsMeaning;
        calls.AddChild(Ui.Label(Tr.Get("Close calls"), TextStyle.Small));
        calls.AddChild(Ui.Spacer());
        if (risk.LowestHp == null)
            calls.AddChild(Ui.Label(NoFightSurvived, TextStyle.BodyStrong));
        else
        {
            calls.AddChild(Ui.Label(CallsPercent(risk.CloseCalls!.Point), TextStyle.BodyStrong));
            calls.AddChild(Ui.Label(CallsRange(risk.CloseCalls), TextStyle.Tiny));
        }
        if (risk.Warning)
            block.AddChild(
                Alert(Tr.Get("Highest hit reaches lowest HP"), Look.Risky, Look.RiskyFill, RiskMark.Why(risk))
            );
        else if (risk.LowestHp == null)
            block.AddChild(Alert(Tr.Get("Died every fight"), Look.Deadly, Look.DeadlyFill, null));
        return block;
    }

    /// <summary>A and B per risk value, a bar per side and each side's warning; for a comparison's details.</summary>
    public static VBoxContainer Compare(TargetResult? a, TargetResult? b)
    {
        var block = Ui.Column(6, "Cs2RiskBlock");
        var head = Ui.Add(block, Ui.Row(8));
        head.AddChild(Ui.Label(Tr.Get("Risk"), TextStyle.Heading));
        head.AddChild(Ui.Spacer());
        head.AddChild(SideLine.Make(Tr.Format("{0} → {1}", SideLine.A, SideLine.B), TextStyle.Tiny));
        var ra = a == null ? null : Risk.Of(a);
        var rb = b == null ? null : Risk.Of(b);
        // A side whose trial ran over before any finished has nothing to show, and says so rather than "not recorded".
        string None(TargetResult? t) =>
            t is { HasEstimates: false, Stall: not null } ? Tr.Get("Unfinished") : Tr.Get("Not recorded");
        string Hit(Risk? r, TargetResult? t) => r is { Recorded: true } ? Ui.Number(r.HighestHit) : None(t);
        string Low(Risk? r, TargetResult? t) =>
            r is not { Recorded: true } ? None(t)
            : r.LowestHp is { } low ? Ui.Number(low)
            : Tr.Get("Died every fight");
        string Calls(Risk? r, TargetResult? t) =>
            r is not { Recorded: true, CloseCalls: { } c } ? None(t)
            : r.LowestHp == null ? NoFightSurvived
            : CallsPercent(c.Point);
        block.AddChild(
            Line(Tr.Get("Highest hit"), Hit(ra, a), Hit(rb, b), Change(ra?.HighestHit, rb?.HighestHit, ra, rb))
        );
        block.AddChild(Line(Tr.Get("Lowest HP"), Low(ra, a), Low(rb, b), Change(ra?.LowestHp, rb?.LowestHp, ra, rb)));
        var scale = Math.Max(a is null || ra is null ? 1 : Scale(a, ra), b is null || rb is null ? 1 : Scale(b, rb));
        foreach (var (side, r, t) in new[] { ("A", ra, a), ("B", rb, b) })
        {
            var row = Ui.Add(block, Ui.Row(6));
            var letter = Ui.Label(side, TextStyle.Tiny);
            letter.LabelSettings = SideLine.Gold(TextStyle.Tiny);
            row.AddChild(letter);
            if (r is { Recorded: true })
                row.AddChild(HpBar.Make("Cs2RiskBar" + side, r.LowestHp, r.HighestHit, scale));
            else
                row.AddChild(Ui.Label(None(t), TextStyle.Tiny));
        }
        block.AddChild(HpBar.Legend(scale));
        block.AddChild(Line(Tr.Get("Close calls"), Calls(ra, a), Calls(rb, b), ""));
        var ranges = new[] { ("A", ra), ("B", rb) }
            .Where(p => p.Item2 is { Recorded: true, CloseCalls: not null, LowestHp: not null })
            .Select(p => Tr.Format("{0} {1}", SideLine.Of(p.Item1), CallsRange(p.Item2!.CloseCalls!)));
        var rangeLine = (HBoxContainer)SideLine.Make(string.Join(" · ", ranges), TextStyle.Tiny);
        rangeLine.Alignment = BoxContainer.AlignmentMode.End;
        block.AddChild(rangeLine);
        foreach (var (side, r, t) in new[] { ("A", ra, a), ("B", rb, b) })
        {
            var text =
                r is not { Recorded: true } && t is { HasEstimates: false, Stall: not null }
                    // Translators: A comparison's risk warning line for a side whose trial ran over before any finished.
                    ? Tr.Format("{0} · unfinished", SideLine.Of(side))
                : r is not { Recorded: true } ? Tr.Format("{0} · not recorded", SideLine.Of(side))
                : r.Warning ? Tr.Format("{0} · highest hit reaches lowest HP", SideLine.Of(side))
                : r.LowestHp == null ? Tr.Format("{0} · died every fight", SideLine.Of(side))
                : Tr.Format("{0} · no warning", SideLine.Of(side));
            var colour =
                r is { Warning: true } ? Look.Risky
                : r is { Recorded: true, LowestHp: null } ? Look.Deadly
                : Colors.Transparent;
            var fill =
                r is { Warning: true } ? Look.RiskyFill
                : r is { Recorded: true, LowestHp: null } ? Look.DeadlyFill
                : Colors.Transparent;
            var alert = Alert(text, colour, fill, r is { Warning: true } ? RiskMark.Why(r) : null);
            alert.Name = "Cs2RiskWarning" + side;
            block.AddChild(alert);
        }
        return block;
    }

    // The bar's scale: the player's max HP when known, else enough for both values.
    private static long Scale(TargetResult result, Risk risk) =>
        result.MaxHp ?? Math.Max(1, Math.Max(risk.HighestHit, risk.LowestHp ?? 0));

    /// <summary>What a close call is, for the hover on its label and value and for the explainer.</summary>
    public static string CloseCallsMeaning =>
        Tr.Get(
            "Fights won where one more hit as large as the highest taken would have killed. Share of all fights, with its 95% range. A lost fight isn't one."
        );

    // In place of a share: a close call is a fight won, and none was.
    private static string NoFightSurvived => Tr.Get("No fight survived");

    private static string CallsPercent(double share) => Tr.Format("{0}%", (share * 100).ToString("0.0", Tr.Culture));

    private static string CallsRange(Estimate e) =>
        Tr.Format("{0}–{1}%", [(e.Low * 100).ToString("0.0", Tr.Culture), (e.High * 100).ToString("0.0", Tr.Culture)]);

    // "+49" from a to b; "same" when equal; empty when either side didn't record it.
    private static string Change(long? a, long? b, Risk? ra, Risk? rb)
    {
        if (ra is not { Recorded: true } || rb is not { Recorded: true } || a is not { } x || b is not { } y)
            return "";
        if (x == y)
            return Tr.Get("same");
        var d = y - x;
        return (d > 0 ? "+" : "") + Ui.Number(d);
    }

    private static HFlowContainer Line(string name, string a, string b, string change)
    {
        var values = Ui.Row(6);
        values.AddChild(Ui.Label(Tr.Format("{0} → {1}", [a, b]), TextStyle.BodyStrong));
        var diff = Ui.Add(values, Ui.Label(change, TextStyle.Tiny));
        diff.CustomMinimumSize = new Vector2(40, 0);
        diff.HorizontalAlignment = HorizontalAlignment.Right;
        return Ui.NameLine(Ui.Label(name, TextStyle.Small), values);
    }

    private static HBoxContainer NotRecorded(string name)
    {
        var row = Ui.Row(6);
        row.AddChild(Ui.Label(name, TextStyle.Small));
        row.AddChild(Ui.Spacer());
        row.AddChild(Ui.Label(Tr.Get("Not recorded"), TextStyle.Small));
        return row;
    }

    /// <summary>
    /// A boxed line with a "!" first, bordered in <paramref name="colour"/> on <paramref name="fill"/>, with optional
    /// hover text <paramref name="why"/>.
    /// </summary>
    private static PanelContainer Alert(string text, Color colour, Color fill, string? why)
    {
        var box = new PanelContainer { Name = "Cs2RiskWarning" };
        PixelBox.Panel(
            box,
            new StyleBoxFlat
            {
                BgColor = fill with { A = fill.A * 0.8f },
                BorderColor = colour.A > 0 ? colour : Colors.White with { A = 0.15f },
                BorderWidthBottom = 1,
                BorderWidthTop = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 3,
                ContentMarginBottom = 3,
            }
        );
        var row = Ui.Add(box, Ui.Row(6));
        if (colour.A > 0)
        {
            var bang = Ui.Label("!", TextStyle.BodyStrong);
            bang.LabelSettings = Panel.Compare.CompareText.Coloured(colour, TextStyle.BodyStrong);
            row.AddChild(bang);
        }
        var line = SideLine.Make(text, TextStyle.Label, wrap: true);
        line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // Level with the mark beside it, which is taller than a line of this text.
        line.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(line);
        if (why != null)
        {
            box.TooltipText = why;
            box.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        return box;
    }
}
