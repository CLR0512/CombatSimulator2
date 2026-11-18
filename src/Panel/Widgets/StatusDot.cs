using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A small round status light: steady, or pulsing while something is under way.</summary>
internal static class StatusDot
{
    private const string ColourMeta = "cs2_dot_colour";
    private const string PulseMeta = "cs2_dot_pulse";
    private const float Size = 10;

    public static Control Make(string name)
    {
        var dot = new Control
        {
            Name = name,
            CustomMinimumSize = new Vector2(Size, Size),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        dot.Draw += () => Paint(dot);
        return dot;
    }

    public static void Set(Control dot, Color colour, bool pulse)
    {
        dot.SetMeta(ColourMeta, colour);
        dot.SetMeta(PulseMeta, pulse);
        dot.QueueRedraw();
    }

    private static void Paint(Control dot)
    {
        var colour = dot.HasMeta(ColourMeta) ? dot.GetMeta(ColourMeta).AsColor() : Look.Running;
        var pulse = dot.HasMeta(PulseMeta) && dot.GetMeta(PulseMeta).AsBool() && dot.IsVisibleInTree();
        var centre = dot.Size / 2;
        if (pulse && Motion.Phase(MotionKind.Pulse) is { } phase)
        {
            // A ring growing out of the dot and fading; redrawn each frame while pulsing.
            dot.DrawCircle(centre, Size / 2 * (1 + phase), colour with { A = 0.45f * (1 - phase) });
            dot.QueueRedraw();
        }
        dot.DrawCircle(centre, Size / 2 - 1, colour);
    }
}
