using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The guided tour: one control at a time, ringed, with everything else dimmed and a popover beside it. The tour
/// presses nothing and blocks clicks while it shows; it follows its control every frame, as the control moves when
/// results arrive or the window changes size.
/// </summary>
internal sealed class Tour
{
    public const int Steps = 5;
    private const float RingPad = 6;

    // The control each step explains, by node name: the first of its names that is showing. The results step rings
    // the chart alone when there is one, not its toolbar and pin tray.
    private static readonly string[][] Targets =
    [
        ["Cs2CardA"],
        ["Cs2StripHeader"],
        ["Cs2Compare", "Cs2CardB"],
        ["Cs2RunControl"],
        ["Cs2ChartPanel", "Cs2CompareChartPanel", "Cs2Results"],
    ];

    // The side each step's popover tries first: the targets button sits high on the window, so its popover goes
    // under it rather than across the strip.
    private static readonly TourSide[] Prefer =
    [
        TourSide.Right,
        TourSide.Below,
        TourSide.Right,
        TourSide.Right,
        TourSide.Right,
    ];

    private readonly Control _layer;
    private readonly Control _window;
    private readonly ColorRect[] _dims = new ColorRect[4];
    private readonly Control _block;
    private readonly Godot.Panel _ring;
    private readonly TourStep _popover;
    private Control? _target;
    private int _targetStep;
    private string _shownKey = "";

    public Tour(Control layer, Control window, Action next, Action back, Action skip)
    {
        _layer = layer;
        _window = window;
        for (var i = 0; i < _dims.Length; i++)
            _dims[i] = Ui.Add(layer, HelpLayer.Dim(i == 0 ? "Cs2TourDim" : "Cs2TourDim" + i));
        _block = Ui.Add(layer, new Control { Name = "Cs2TourBlock", MouseFilter = Control.MouseFilterEnum.Stop });
        _ring = Ui.Add(layer, new Godot.Panel { Name = "Cs2TourRing", MouseFilter = Control.MouseFilterEnum.Ignore });
        PixelBox.Panel(
            _ring,
            new StyleBoxFlat
            {
                BgColor = Colors.Transparent,
                BorderColor = Look.Safe,
                BorderWidthLeft = 3,
                BorderWidthTop = 3,
                BorderWidthRight = 3,
                BorderWidthBottom = 3,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2,
            }
        );
        _popover = new TourStep(HelpText.SkipTour, HelpText.Back);
        _popover.Next += next;
        _popover.Back += back;
        _popover.Skip += skip;
        layer.AddChild(_popover.Root);
        _popover.NextButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Where the popover went last, for <c>cs2 ui</c>.</summary>
    public TourSide? Side { get; private set; }

    /// <summary>The name of the control ringed, for <c>cs2 ui</c>; "-" when none is.</summary>
    public string Ringed => _target is { } t && GodotObject.IsInstanceValid(t) && t.IsVisibleInTree() ? t.Name : "-";

    /// <summary>Lays out the step: finds its control, rings it, places the popover and shows the step's text.</summary>
    public void Update(int step)
    {
        var target = Find(step);
        var alternate = step switch
        {
            3 => PanelState.B != null,
            4 => RunService.Instance.IsActive,
            5 => _window.FindChild("Cs2Empty", true, false) is Control { } empty && empty.IsVisibleInTree(),
            _ => false,
        };
        var prefs = PanelState.Prefs;
        var key =
            $"{step}:{alternate}:{prefs.ResultsMode}:{prefs.Trials}:{prefs.SimulateIn}:{prefs.StopEarly}:{Tr.Version}";
        if (key != _shownKey)
        {
            _shownKey = key;
            var (title, text) = HelpText.Step(step, alternate, PanelState.Prefs.ResultsMode == "chart");
            _popover.Show(
                HelpText.Count(step, Steps),
                title,
                text,
                canGoBack: step > 1,
                step == Steps ? HelpText.Done : HelpText.Next
            );
            _popover.NextButton.CallDeferred(Control.MethodName.GrabFocus);
        }

        var room = new TourRect(0, 0, _layer.Size.X, _layer.Size.Y);
        TourRect ring;
        if (target != null)
        {
            var toLayer = _layer.GetGlobalTransform().AffineInverse();
            var rect = target.GetGlobalRect();
            var start = toLayer * rect.Position;
            var end = toLayer * rect.End;
            ring = new TourRect(start.X, start.Y, end.X - start.X, end.Y - start.Y).Grow(RingPad);
        }
        else
        {
            // Not built yet (the window is still building): everything dimmed, the popover in the middle.
            ring = new TourRect(room.Width / 2, room.Height / 2, 0, 0);
        }
        _ring.Visible = target != null;
        _block.Visible = target != null;
        Place(_ring, ring);
        Place(_block, ring);
        Place(_dims[0], new TourRect(0, 0, room.Width, ring.Y));
        Place(_dims[1], new TourRect(0, ring.Bottom, room.Width, room.Height - ring.Bottom));
        Place(_dims[2], new TourRect(0, ring.Y, ring.X, ring.Height));
        Place(_dims[3], new TourRect(ring.Right, ring.Y, room.Width - ring.Right, ring.Height));

        var size = _popover.Root.GetCombinedMinimumSize();
        var (x, y, side) =
            target != null
                ? TourPlacement.Place(room, ring, size.X, size.Y, Prefer[step - 1])
                : (Math.Max(0, (room.Width - size.X) / 2), Math.Max(0, (room.Height - size.Y) / 2), TourSide.Over);
        _popover.Root.Position = new Vector2(x, y).Round();
        _popover.Root.Size = size;
        Side = side;
    }

    private Control? Find(int step)
    {
        if (
            _targetStep != step
            || _target == null
            || !GodotObject.IsInstanceValid(_target)
            || !_target.IsVisibleInTree()
            || !_window.IsAncestorOf(_target)
        )
        {
            _targetStep = step;
            _target = null;
            foreach (var name in Targets[Math.Clamp(step, 1, Steps) - 1])
                if (_window.FindChild(name, true, false) is Control { } found && found.IsVisibleInTree())
                {
                    _target = found;
                    break;
                }
        }
        return _target;
    }

    private static void Place(Control control, TourRect rect)
    {
        control.Position = new Vector2(rect.X, rect.Y);
        control.Size = new Vector2(Math.Max(0, rect.Width), Math.Max(0, rect.Height));
    }
}
