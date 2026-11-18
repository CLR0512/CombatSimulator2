using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// How the panel shows that something is being worked out again: what was last shown stays where it is, faded,
/// until the new answer replaces it. Nothing is added, removed or resized for the wait, so nothing around it moves.
/// </summary>
internal static class Pending
{
    private const float Faded = 0.45f;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CanvasItem, Tween> _pulses = [];

    /// <summary>
    /// Breathes <paramref name="shown"/> between faded and nearly full while <paramref name="waiting"/>, for
    /// something that is being read and has nothing to show yet but its shape; restores it after.
    /// </summary>
    public static void Pulse(CanvasItem shown, bool waiting)
    {
        if (_pulses.TryGetValue(shown, out var running))
        {
            if (waiting)
                return;
            running.Kill();
            _pulses.Remove(shown);
        }
        if (!waiting)
        {
            shown.Modulate = Colors.White;
            return;
        }
        var half = Motion.Seconds(MotionKind.Breathe) / 2;
        var pulse = shown.CreateTween();
        if (half <= 0)
        {
            // Standing still it stays faded, which says the same without moving.
            pulse.TweenProperty(shown, "modulate:a", Faded, 0);
        }
        else
        {
            pulse.SetLoops();
            pulse.TweenProperty(shown, "modulate:a", Faded, half).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(shown, "modulate:a", 0.85f, half).SetTrans(Tween.TransitionType.Sine);
        }
        _pulses.Add(shown, pulse);
    }

    /// <summary>
    /// A bar standing where something not read yet will be: as tall as it, and as wide as its place unless
    /// <paramref name="width"/> says otherwise.
    /// </summary>
    public static Godot.Panel Bar(float width, float height)
    {
        var bar = new Godot.Panel
        {
            CustomMinimumSize = new Vector2(width, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var box = new StyleBoxFlat { BgColor = Colors.White with { A = 0.05f } };
        box.SetCornerRadiusAll(3);
        PixelBox.Panel(bar, box);
        return bar;
    }

    /// <summary>Fades <paramref name="shown"/> while <paramref name="pending"/>, and restores it after.</summary>
    public static void Show(CanvasItem shown, bool pending) =>
        shown.Modulate = Colors.White with { A = pending ? Faded : 1f };
}
