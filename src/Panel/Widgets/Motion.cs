using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// Short animations: values easing to new ones, numbers counting up, views fading in. Each takes its kind's
/// length at the level the player chose, so a level turns all of them down or off together. One tween per node at
/// a time (a new one replaces it), skipped when the node isn't showing so hidden updates cost nothing.
/// </summary>
internal static class Motion
{
    private static readonly Dictionary<ulong, Tween> _tweens = [];

    public static MotionLevel Level { get; private set; } = MotionLevel.Full;

    public static void SetLevel(MotionLevel level) => Level = level;

    /// <summary>How long <paramref name="kind"/> takes now; 0 is at once.</summary>
    public static double Seconds(MotionKind kind) => MotionTimes.Seconds(kind, Level);

    /// <summary>
    /// How far through its round a repeating <paramref name="kind"/> is, 0…1; null when it stands still.
    /// </summary>
    public static float? Phase(MotionKind kind)
    {
        var ms = (ulong)(Seconds(kind) * 1000);
        return ms == 0 ? null : (float)(Time.GetTicksMsec() % ms / (double)ms);
    }

    /// <summary>
    /// Calls <paramref name="apply"/> with values easing from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    public static void Ease(Node node, double from, double to, MotionKind kind, Action<double> apply)
    {
        var id = node.GetInstanceId();
        if (_tweens.Remove(id, out var old) && GodotObject.IsInstanceValid(old))
            old.Kill();
        var seconds = Seconds(kind);
        if (
            from.Equals(to)
            || seconds <= 0
            || !node.IsInsideTree()
            || node is CanvasItem { Visible: false }
            || node is CanvasItem item && !item.IsVisibleInTree()
        )
        {
            apply(to);
            return;
        }
        var tween = node.CreateTween();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenMethod(Callable.From<double>(apply), from, to, seconds);
        tween.Finished += () => _tweens.Remove(id);
        _tweens[id] = tween;
    }

    /// <summary>Fades a control in from transparent.</summary>
    public static void FadeIn(CanvasItem item, MotionKind kind = MotionKind.Fade)
    {
        var id = item.GetInstanceId();
        if (_tweens.Remove(id, out var old) && GodotObject.IsInstanceValid(old))
            old.Kill();
        var seconds = Seconds(kind);
        if (seconds <= 0 || !item.IsInsideTree())
        {
            item.Modulate = item.Modulate with { A = 1 };
            return;
        }
        item.Modulate = item.Modulate with { A = 0 };
        var tween = item.CreateTween();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(item, "modulate:a", 1.0, seconds);
        tween.Finished += () => _tweens.Remove(id);
        _tweens[id] = tween;
    }

    /// <summary>
    /// Shows or hides a control: it fades in, and fades out before it goes. <paramref name="item"/> counts as
    /// hidden from the call on, whatever is still fading.
    /// </summary>
    public static void Reveal(CanvasItem item, bool show, MotionKind kind)
    {
        var id = item.GetInstanceId();
        if (show)
        {
            item.Visible = true;
            FadeIn(item, kind);
            return;
        }
        if (_tweens.Remove(id, out var old) && GodotObject.IsInstanceValid(old))
            old.Kill();
        var seconds = Seconds(kind);
        if (seconds <= 0 || !item.IsVisibleInTree())
        {
            item.Visible = false;
            return;
        }
        var tween = item.CreateTween();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(item, "modulate:a", 0.0, seconds);
        tween.Finished += () =>
        {
            _tweens.Remove(id);
            item.Visible = false;
        };
        _tweens[id] = tween;
    }

    private const string ValueMeta = "cs2_value";

    /// <summary>Counts a label's number up or down to <paramref name="value"/>, formatted each step.</summary>
    public static void CountTo(Label label, double value, Func<double, string> format)
    {
        var from = label.HasMeta(ValueMeta) ? label.GetMeta(ValueMeta).AsDouble() : 0;
        label.SetMeta(ValueMeta, value);
        if (!double.IsFinite(value) || !double.IsFinite(from))
        {
            Ease(label, 0, 0, MotionKind.Count, _ => label.Text = format(value));
            return;
        }
        Ease(label, from, value, MotionKind.Count, v => label.Text = format(v));
    }

    /// <summary>Forgets a label's last number, so the next value shows at once.</summary>
    public static void ResetCount(Label label) => label.RemoveMeta(ValueMeta);

    public static void Clear() => _tweens.Clear();
}
