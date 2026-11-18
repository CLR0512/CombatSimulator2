using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>The words and numbers a comparison shows, in the game's language.</summary>
internal static class CompareText
{
    public static string Verdict(Verdict verdict) =>
        verdict switch
        {
            Model.Verdict.Better => Tr.Get("Better", "comparison verdict"),
            Model.Verdict.Worse => Tr.Get("Worse", "comparison verdict"),
            Model.Verdict.NoClearDifference => Tr.Get("Unclear", "verdict"),
            _ => "–",
        };

    /// <summary>A difference with its sign: "+468", "−139", "0".</summary>
    public static string Signed(double value)
    {
        if (!double.IsFinite(value))
            return "–";
        if (value == 0)
            return Ui.Number(0);
        return (value > 0 ? "+" : "−") + Ui.Number(Math.Abs(value));
    }

    /// <summary>A share with its sign: "+31%", "−0.4%".</summary>
    public static string SignedPercent(double fraction)
    {
        if (!double.IsFinite(fraction))
            return "–";
        var text = Ui.Percent(Math.Abs(fraction));
        // A bound that rounds to 0 has no sign: "0%", never "−0%".
        return fraction == 0 || text == Ui.Percent(0) ? text : (fraction > 0 ? "+" : "−") + text;
    }

    /// <summary>A percentage with its range: "+31%  (+28% to +34%)", or "" without a percentage.</summary>
    public static string Percent(Estimate? percent) =>
        percent == null
            ? ""
            : Tr.Format(
                "{0}  ({1} to {2})",
                [SignedPercent(percent.Point), SignedPercent(percent.Low), SignedPercent(percent.High)]
            );

    /// <summary>Half a range's width: "± 21", or "" without a range.</summary>
    public static string Range(Estimate? estimate)
    {
        if (estimate == null)
            return "";
        var half = (estimate.High - estimate.Low) / 2;
        return double.IsFinite(half) && half > 0 ? Tr.Format("± {0}", Ui.Number(half)) : "";
    }

    /// <summary>A side's name for headers: "A · Character as is", "B · saved run".</summary>
    public static string Side(string letter, Run run, bool saved) =>
        saved ? Tr.Format("{0} · saved run", letter) : string.Join(" · ", letter, Words.Setup(run.Setup));

    /// <summary>A side's value, or why it has none, for a row.</summary>
    public static string Value(Estimate? estimate, TargetState state) =>
        estimate != null ? Ui.Number(estimate.Point)
        : state is TargetState.Building or TargetState.Fighting ? Tr.Get("Running…")
        : state == TargetState.Queued ? Tr.Get("Queued")
        : "–";

    /// <summary>The verdict badge: the verdict's colour on its fill, the word always written.</summary>
    public static PanelContainer Badge(string name = "")
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (name.Length > 0)
            panel.Name = name;
        panel.AddChild(Ui.Label("", TextStyle.Label));
        SetBadge(panel, Model.Verdict.None);
        return panel;
    }

    private static readonly Dictionary<Color, LabelSettings> _styles = [];

    /// <summary>Disposes the label settings made here when the game quits (they hold CS2's fonts).</summary>
    public static void Release()
    {
        foreach (var settings in _styles.Values)
            settings.Dispose();
        _styles.Clear();
    }

    /// <summary>
    /// The badge for one target's difference: its verdict, or, where a side died every fight, which side did, in
    /// the mark's red edge with light text. A value earned while dying is neither better nor worse.
    /// </summary>
    public static void SetBadge(PanelContainer badge, Difference difference)
    {
        if (difference.StallA != null || difference.StallB != null)
        {
            // Green or red when only one side can kill the target; neutral when neither can.
            SetBadge(badge, difference.Verdict);
            var sides = new List<string>();
            if (difference.StallA is { } a)
                sides.Add("A: " + Tr.Note(StallReasons.Brief(a.Reason)));
            if (difference.StallB is { } b)
                sides.Add("B: " + Tr.Note(StallReasons.Brief(b.Reason)));
            ((Label)badge.GetChild(0)).Text = string.Join("\n", sides);
            badge.TooltipText = string.Join(
                "\n",
                new[] { difference.ReasonsA, difference.ReasonsB }.Where(r => r != null).Select(r => Tr.Notes(r!, " "))
            );
            return;
        }
        if (difference.Reason is not { } reason)
        {
            SetBadge(badge, difference.Verdict);
            badge.TooltipText = "";
            return;
        }
        var label = (Label)badge.GetChild(0);
        var box = new StyleBoxFlat
        {
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
            BgColor = Look.DeadlyFill,
            BorderColor = Look.Deadly,
        };
        box.SetBorderWidthAll(1);
        label.Text = Tr.Note(reason);
        label.LabelSettings = Coloured(Look.Deadly.Lerp(Colors.White, 0.75f));
        PixelBox.Panel(badge, box);
        badge.TooltipText = Results.DiedMark.Why;
    }

    public static void SetBadge(PanelContainer badge, Verdict verdict)
    {
        var label = (Label)badge.GetChild(0);
        var colour = DiffBar.Colour(verdict, label);
        var strong = verdict is Model.Verdict.Better or Model.Verdict.Worse;
        var box = new StyleBoxFlat
        {
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BgColor = strong ? colour.Darkened(0.6f) : Colors.Black with { A = 0.25f },
            BorderColor = strong ? colour : colour with { A = 0.35f },
            DrawCenter = verdict != Model.Verdict.None,
        };
        label.Text = Verdict(verdict);
        // A light tint of the verdict on its dark fill, which the verdict colour itself is hard to read on.
        label.LabelSettings = Coloured(strong ? colour.Lerp(Colors.White, 0.75f) : Look.Muted(label));
        PixelBox.Panel(badge, box);
    }

    /// <summary>A label style in a fixed colour that mElvUI's skin leaves alone.</summary>
    public static LabelSettings Coloured(Color colour, TextStyle style = TextStyle.Label)
    {
        var key = colour with { A = colour.A + (int)style * 1e-4f };
        if (!_styles.TryGetValue(key, out var settings))
        {
            settings = (LabelSettings)Look.Style(style).Duplicate();
            Look.MarkSkipped(settings);
            settings.FontColor = colour;
            _styles[key] = settings;
        }
        settings.FontSize = Look.Style(style).FontSize;
        return settings;
    }

    public static void Clear() => _styles.Clear();
}
