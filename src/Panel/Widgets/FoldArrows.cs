using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A flat button of two drawn arrows: pointing out (◀ ▶) to unfold, pointing in (▶ ◀) to fold; brighter under the
/// pointer.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class FoldArrows
{
    private const float ArrowWidth = 6;
    private const float HalfHeight = 7;
    private const float Gap = 3;

    private readonly Button _button;
    private readonly Control _arrows;
    private bool _outward;

    public FoldArrows(string name, Action pressed)
    {
        _button = Ui.Button("", "", name, pressed);
        _button.Flat = true;
        _arrows = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _arrows.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _arrows.Draw += Paint;
        _button.AddChild(_arrows);
        _button.MouseEntered += _arrows.QueueRedraw;
        _button.MouseExited += _arrows.QueueRedraw;
    }

    public Button Root => _button;

    /// <summary>Points the arrows out when <paramref name="unfold"/>, in otherwise, with the hover text that says so.</summary>
    public void Show(bool unfold, string tooltip)
    {
        _outward = unfold;
        _button.TooltipText = tooltip;
        _arrows.QueueRedraw();
    }

    private void Paint()
    {
        var c = _arrows.Size / 2;
        // The text's own colour, toned down like the chart's other marks so the arrows don't outshine the bars.
        var colour = Look.Text(_arrows) with
        {
            A = _button.IsHovered() ? 0.85f : 0.45f,
        };
        foreach (var side in new[] { -1f, 1f })
        {
            var inner = c.X + side * Gap;
            var outer = c.X + side * (Gap + ArrowWidth);
            var (tip, back) = _outward ? (outer, inner) : (inner, outer);
            Vector2[] points = [new(tip, c.Y), new(back, c.Y - HalfHeight), new(back, c.Y + HalfHeight)];
            _arrows.DrawColoredPolygon(points, colour);
            // A filled polygon has hard steps on its slanted edges: an anti-aliased outline smooths them.
            _arrows.DrawPolyline([.. points, points[0]], colour, 1, antialiased: true);
        }
    }
}
