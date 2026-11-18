using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A word that rises from a control and fades ("Copied!"), drawn over CS2's window so nothing around the control
/// moves to make room for it.
/// </summary>
internal static class FloatNote
{
    private const float Rise = 18;

    // How long the word stays readable between fading in and out.
    private const double Hold = 0.7;

    /// <summary>Shows <paramref name="text"/> centred above <paramref name="from"/>, rising as it fades in and out.</summary>
    public static void Show(Control from, string text)
    {
        if (!from.IsInsideTree() || Host(from) is not { } host)
            return;
        // The word alone, with a dark edge so it reads over whatever is under it.
        var note = Ui.Label(text, TextStyle.Small, "Cs2FloatNote");
        var settings = (LabelSettings)note.LabelSettings.Duplicate();
        settings.OutlineSize = 5;
        settings.OutlineColor = Colors.Black with { A = 0.85f };
        note.LabelSettings = settings;
        note.TopLevel = true;
        note.ZIndex = 30;
        note.MouseFilter = Control.MouseFilterEnum.Ignore;
        note.Modulate = Colors.White with { A = 0 };
        host.AddChild(note);
        var scale = host.GetGlobalTransform().Scale;
        note.Scale = scale;
        note.ResetSize();
        var anchor = from.GetGlobalRect();
        var size = note.Size * scale;
        var start = new Vector2(anchor.GetCenter().X - size.X / 2, anchor.Position.Y - size.Y - 2).Round();
        note.GlobalPosition = start;
        var fade = Motion.Seconds(MotionKind.Fade);
        var tween = note.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(note, "modulate:a", 1f, fade);
        // It rises as it fades in and again as it fades out, each move as short as the fade, and stands still
        // between; only where everything else moves too.
        if (Motion.Level == MotionLevel.Full)
        {
            var half = new Vector2(0, Rise * scale.Y / 2);
            tween.TweenProperty(note, "global_position", start - half, fade);
            tween.TweenProperty(note, "global_position", start - 2 * half, fade).SetDelay(fade + Hold);
        }
        tween.TweenProperty(note, "modulate:a", 0f, fade).SetDelay(fade + Hold);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(note.QueueFree));
    }

    // CS2's window, which the note is drawn over.
    private static Control? Host(Control from)
    {
        Control? top = null;
        for (Node? node = from; node != null; node = node.GetParent())
            if (node is Control control)
            {
                top = control;
                if (control.Name == "Cs2Root")
                    break;
            }
        return top;
    }
}
