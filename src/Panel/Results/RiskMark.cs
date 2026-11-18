using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The warning mark: a small "!" box beside a target. Orange when its highest hit reaches its lowest HP; red when
/// every fight died, where there is no lowest HP to reach.
/// </summary>
internal static class RiskMark
{
    public static PanelContainer Make(string name)
    {
        var box = new PanelContainer { Name = name, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var mark = Ui.Label("!", TextStyle.BodyStrong);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        box.AddChild(mark);
        box.MouseFilter = Control.MouseFilterEnum.Stop;
        Colour(box, died: false);
        box.TooltipText = Tr.Get("Highest hit reaches lowest HP");
        return box;
    }

    /// <summary>
    /// Shows the mark for a target's risk: hidden without a warning, unless every fight <paramref name="died"/>.
    /// </summary>
    public static void Set(PanelContainer mark, Risk? risk, bool died)
    {
        mark.Visible = died || risk is { Warning: true };
        if (!mark.Visible)
            return;
        Colour(mark, died);
        mark.TooltipText = died ? DiedMark.Text : Why(risk!);
    }

    private static void Colour(PanelContainer box, bool died)
    {
        var edge = died ? Look.Deadly : Look.Risky;
        var style = new StyleBoxFlat
        {
            BgColor = died ? Look.DeadlyFill : Look.RiskyFill,
            BorderColor = edge,
            ContentMarginLeft = 5,
            ContentMarginRight = 5,
        };
        style.SetBorderWidthAll(1);
        PixelBox.Panel(box, style);
        // A light tint of the edge: the colour itself on its own fill is hard to read.
        ((Label)box.GetChild(0)).LabelSettings = Compare.CompareText.Coloured(
            edge.Lerp(Colors.White, 0.75f),
            TextStyle.BodyStrong
        );
    }

    /// <summary>Why a target warns, for hover text.</summary>
    public static string Why(Risk risk) =>
        Tr.Format(
            "The biggest hit taken here ({0}) is at least the lowest HP reached ({1}): that hit at that moment would kill.",
            [Ui.Number(risk.HighestHit), Ui.Number(risk.LowestHp ?? 0)]
        );
}
