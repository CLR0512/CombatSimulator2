using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A scroll container's bar shown only while it scrolls or the pointer is over the bar, then faded out. Its lane
/// is kept, a little apart from the content, whether or not there is anything to scroll: nothing resizes as it comes
/// and goes.
/// </summary>
internal static class QuietScrollBar
{
    // How long the bar stays after the last scroll.
    private const double Linger = 0.8;

    public static void Apply(ScrollContainer scroll)
    {
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Reserve;
        scroll.AddThemeConstantOverride("scrollbar_v_separation", 8);
        var bar = scroll.GetVScrollBar();
        bar.Modulate = Colors.White with { A = 0 };
        var lastMoved = double.NegativeInfinity;
        var wasShown = false;
        Tween? fade = null;
        bar.ValueChanged += _ => lastMoved = Time.GetTicksMsec() / 1000.0;
        // Polled rather than told: a faded bar under the pointer is easier to read off its rectangle.
        var timer = new Godot.Timer { WaitTime = 0.1, Autostart = true };
        timer.Timeout += () =>
        {
            if (!scroll.IsVisibleInTree())
                return;
            // Over the bar's own lane, a little wider than the bar to be easy to find: over the content it would show
            // nearly all the time.
            var over = bar.IsVisibleInTree() && bar.GetGlobalRect().Grow(6).HasPoint(bar.GetGlobalMousePosition());
            var moving = Time.GetTicksMsec() / 1000.0 - lastMoved < Linger;
            var shown = over || moving;
            if (shown == wasShown)
                return;
            wasShown = shown;
            fade?.Kill();
            fade = bar.CreateTween();
            fade.TweenProperty(bar, "modulate:a", shown ? 1f : 0f, Motion.Seconds(MotionKind.Fade));
        };
        scroll.AddChild(timer);
    }
}
