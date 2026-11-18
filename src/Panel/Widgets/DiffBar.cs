using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A difference drawn around 0: a line in the middle for 0, a solid bar from 0 to the difference, and a band behind
/// it for its 95% range. Green when B is better, red when worse, plain when it's unclear.
/// </summary>
internal static class DiffBar
{
    private const string DeltaMeta = "cs2_delta";
    private const string LowMeta = "cs2_low";
    private const string HighMeta = "cs2_high";
    private const string ScaleMeta = "cs2_scale";
    private const string VerdictMeta = "cs2_verdict";

    public static Control Make(string name = "", float height = 12)
    {
        var bar = new Control
        {
            CustomMinimumSize = new Vector2(120, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (name.Length > 0)
            bar.Name = name;
        bar.Draw += () => Paint(bar);
        bar.ThemeChanged += bar.QueueRedraw;
        return bar;
    }

    /// <summary>
    /// Shows <paramref name="delta"/> (null: only the 0 line) on a scale where <paramref name="scale"/> reaches the
    /// bar's end on either side.
    /// </summary>
    public static void Set(Control bar, Estimate? delta, double scale, Verdict verdict)
    {
        if (delta == null || !double.IsFinite(delta.Point) || scale <= 0)
        {
            bar.SetMeta(DeltaMeta, double.NaN);
        }
        else
        {
            bar.SetMeta(DeltaMeta, delta.Point);
            bar.SetMeta(LowMeta, delta.Low);
            bar.SetMeta(HighMeta, delta.High);
        }
        bar.SetMeta(ScaleMeta, scale);
        bar.SetMeta(VerdictMeta, (int)verdict);
        bar.QueueRedraw();
    }

    /// <summary>The scale for a set of differences: the furthest end of any range, so every bar fits.</summary>
    public static double ScaleOf(IEnumerable<Estimate?> deltas)
    {
        var max = 0.0;
        foreach (var d in deltas)
        {
            if (d == null || !double.IsFinite(d.Point))
                continue;
            foreach (var v in new[] { d.Point, d.Low, d.High })
                if (double.IsFinite(v))
                    max = Math.Max(max, Math.Abs(v));
        }
        return max;
    }

    /// <summary>The colour of a verdict: green better, red worse, otherwise plain.</summary>
    public static Color Colour(Verdict verdict, Control? control = null) =>
        verdict switch
        {
            Verdict.Better => Look.Safe,
            Verdict.Worse => Look.Deadly,
            _ => control != null ? Look.Muted(control) : Colors.White with { A = 0.6f },
        };

    private static void Paint(Control bar)
    {
        var size = bar.Size;
        var track = new Rect2(Vector2.Zero, size);
        if (Look.Track(bar) is StyleBoxTexture or null)
            bar.PxRect(track, Colors.Black with { A = 0.55f });
        else
            Look.Track(bar).Draw(bar.GetCanvasItem(), track);
        var mid = size.X / 2;
        var delta = bar.HasMeta(DeltaMeta) ? bar.GetMeta(DeltaMeta).AsDouble() : double.NaN;
        if (double.IsFinite(delta))
        {
            var scale = bar.GetMeta(ScaleMeta).AsDouble();
            var verdict = (Verdict)bar.GetMeta(VerdictMeta).AsInt32();
            var colour = Colour(verdict, bar);
            float X(double v) => (float)Math.Clamp(mid + v / scale * (mid - 2), 1, size.X - 1);
            var low = bar.GetMeta(LowMeta).AsDouble();
            var high = bar.GetMeta(HighMeta).AsDouble();
            var left = X(double.IsFinite(low) ? low : -scale);
            var right = X(double.IsFinite(high) ? high : scale);
            bar.PxRect(new Rect2(left, 0, Math.Max(2, right - left), size.Y), colour with { A = 0.28f });
            var end = X(delta);
            var inset = size.Y * 0.25f;
            bar.PxRect(
                new Rect2(Math.Min(mid, end), inset, Math.Max(2, Math.Abs(end - mid)), size.Y - 2 * inset),
                colour
            );
        }
        bar.PxLine(new Vector2(mid, 0), new Vector2(mid, size.Y), Look.Text(bar) with { A = 0.85f }, 2);
    }
}
