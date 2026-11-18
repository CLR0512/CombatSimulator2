using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// The results chart, one control drawn by hand. A bar per target with its 95% whisker, coloured by deaths, grouped
/// over bands with each area's art; a folded area draws only its best target, outlined. Targets without a result take
/// a narrow slot and no portrait, and areas where none has a result collapse, neighbours together, into one stub until
/// opened, so the width goes to bars with values. Their slots shrink to fit (12–44 units) and the chart scrolls only
/// below that; portraits and area art keep a readable size and overlap their neighbours slightly when slots are
/// narrower. Clicking a portrait ignores its target in the next run, or brings it back: the portrait is crossed out
/// and the bar greyed. Hover shows the details panel's essentials; click selects, shift-click pins, clicking a band
/// folds or unfolds its area, and the button in the corner under the axis unfolds or folds them all.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class Chart
{
    private const float MinSlot = 12;
    private const float MaxSlot = 44;
    private const float AxisWidth = 58;
    private const float TopPad = 16;
    private const float PortraitRow = 34;
    private const float BandRow = 48;
    private const float LegendRow = 30;
    private const float EmptySlot = 10;
    private const float StubSlot = 24;
    private const float StackStep = 8;
    private const int StackShown = 4;
    private const float IconMin = 22;

    internal sealed record BarItem(
        string Key,
        string Name,
        string Area,
        string AreaEnglish,
        RankRow Rank,
        double DeathRate,
        bool Folded,
        Texture2D? Portrait
    )
    {
        // Refused, unfinished without a finished trial, cancelled: nothing to draw but an outline.
        public bool Empty => Rank.Estimate == null && !Rank.IsRunning && Rank.State != TargetState.Queued;
    }

    /// <param name="Collapsed">No target has a result and the player hasn't opened the area: drawn as a stub.</param>
    internal sealed record Group(
        string Id,
        string Name,
        Texture2D? Art,
        List<BarItem> Bars,
        bool Folded,
        bool Collapsed = false
    )
    {
        public bool AllEmpty => Bars.Count > 0 && Bars.All(b => b.Empty);
    }

    private readonly ScrollContainer _scroll;
    private readonly Control _canvas;
    private readonly HoverPanel _hover;
    private readonly Control _owner;
    private readonly List<Group> _groups = [];
    private readonly List<(Rect2 Rect, BarItem Bar)> _barHits = [];
    private readonly List<(Rect2 Rect, string Key)> _toggleHits = [];
    private readonly FoldArrows _foldAll;
    private readonly TextureRect _hint;

    // A band's click target and the areas it stands for: one, or several collapsed neighbours.
    private readonly List<(Rect2 Rect, IReadOnlyList<Group> Groups)> _bandHits = [];
    private Run? _run;
    private RankTable? _table;
    private TimeUnit _unit;
    private double _max;
    private float _slot;

    public Chart(Control hoverOwner)
    {
        _scroll = new ScrollContainer
        {
            Name = "Cs2ChartScroll",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _canvas = new Control
        {
            Name = "Cs2Chart",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ClipContents = true,
        };
        _canvas.Draw += Paint;
        _canvas.ThemeChanged += _canvas.QueueRedraw;
        _canvas.Resized += Layout;
        _canvas.GuiInput += Input;
        _canvas.MouseExited += () => _hover!.Hide();
        _scroll.Resized += Layout;
        _scroll.AddChild(_canvas);
        ScrollFade.Sideways(_scroll, LegendRow); // the bars fade at an edge with more past it; the legend stays whole
        _foldAll = new FoldArrows("Cs2ChartFoldAll", FoldAll);
        _canvas.AddChild(_foldAll.Root);
        // What the chart does on hover and click, kept off its foot so the bars get the room; quiet until pointed at.
        _hint = Ui.Picture(Ui.Greyscale(Art.File(Art.InfoPath)), 16, "Cs2ChartHint");
        _hint.MouseFilter = Control.MouseFilterEnum.Stop;
        _hint.TooltipText = Tr.Get(
            "Hover a bar for value, range and deaths · click for details · shift-click to pin · click a portrait to ignore it · click an area to fold it into one bar"
        );
        _hint.SelfModulate = Colors.White with { A = 0.45f };
        _hint.MouseEntered += () => _hint.SelfModulate = Colors.White;
        _hint.MouseExited += () => _hint.SelfModulate = Colors.White with { A = 0.45f };
        _canvas.AddChild(_hint);
        _owner = hoverOwner;
        _hover = new HoverPanel(hoverOwner);
    }

    public Control Root => (Control)_scroll.GetParent();

    /// <summary>Raised when a pin is refused, with the reason for the view to show.</summary>
    public event Action<string>? Message;

    public int Bars => _groups.Sum(g => g.Collapsed ? 0 : g.Bars.Count);

    /// <summary>How many of the bars shown are stubs of targets where every fight died.</summary>
    public int Died => Count(b => b.Rank.Died);

    /// <summary>How many bars shown stalled (no trial finished), and how many carry the ran-over mark.</summary>
    public (int Stalled, int RanOver) Stalls => (Count(b => b.Rank.Stall != null), Count(b => b.Rank.RanOver != null));

    private int Count(Func<BarItem, bool> which) => _groups.Sum(g => g.Collapsed ? 0 : g.Bars.Count(which));

    /// <summary>
    /// How many areas are folded, how many have more than one target, and how many are collapsed for lack of results.
    /// </summary>
    public (int Folded, int Foldable, int Collapsed) Folds =>
        (
            _groups.Count(g => g.Folded && !g.Collapsed),
            _groups.Count(g => !g.Collapsed && (g.Bars.Count > 1 || g.Folded)),
            _groups.Count(g => g.Collapsed)
        );

    /// <summary>The portraits that toggle their target, and how many of them are ignored.</summary>
    public (int Toggles, int Ignored) Toggles =>
        (_toggleHits.Count, _toggleHits.Count(h => PanelState.Ignored.Contains(h.Key)));

    public bool Scrolls => _canvas.CustomMinimumSize.X > _scroll.Size.X + 1;

    public void Show(Run run, RankTable table, TimeUnit unit)
    {
        _run = run;
        _table = table;
        _unit = unit;
        _groups.Clear();
        var game = LiveGame.Find();
        _groups.AddRange(
            ChartGroups.Build(
                run,
                table,
                area => game != null ? Art.Area(game, area) : Art.Missing,
                spec => game != null ? Art.Target(game, spec) : Art.Missing,
                PanelState.Prefs.FoldedAreas,
                PanelState.Prefs.OpenedAreas
            )
        );
        // The scale's top comes from the ranked targets: one that died every fight sets no scale.
        _max = _groups
            .SelectMany(g => g.Bars)
            .Where(b => !b.Rank.Died)
            .Select(b => b.Rank.Estimate)
            .OfType<Estimate>()
            .Select(e => double.IsFinite(e.High) ? e.High : e.Point)
            .Where(double.IsFinite)
            .DefaultIfEmpty(0)
            .Max();
        Animate();
        Layout();
        ShowFoldAll();
        _canvas.QueueRedraw();
    }

    // The corner button: unfold every area while any is folded, else fold each area with several targets into its best.
    private void ShowFoldAll()
    {
        var folded = _groups.Any(g => g.Folded && !g.Collapsed);
        _foldAll.Root.Visible = _groups.Any(g => !g.Collapsed && (g.Bars.Count > 1 || g.Folded));
        _foldAll.Show(folded, folded ? Tr.Get("Unfold every area") : Tr.Get("Fold every area into its best bar"));
    }

    private void FoldAll()
    {
        var folds = PanelState.Prefs.FoldedAreas;
        if (_groups.Any(g => g.Folded && !g.Collapsed))
            folds.Clear();
        else
            folds.AddRange(_groups.Where(g => !g.Collapsed && g.Bars.Count > 1).Select(g => g.Id));
        PanelState.Update();
    }

    // Per bar its value and whisker's ends, eased together from the heights on screen to the new ones with the axis.
    private readonly Dictionary<string, Span> _from = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Span> _target = new(StringComparer.Ordinal);

    private readonly record struct Span(double Point, double Low, double High);

    private double _fromTop;
    private double _targetTop;
    private double _t = 1;

    // Eased as shares of the axis, so each bar moves straight from its old height to its new one whatever the axis does:
    // eased by value, a bar from zero ran ahead of an axis easing up from a small top, and showed at full height.
    private Span Shown(string key)
    {
        var (from, to) = (_from.GetValueOrDefault(key), _target.GetValueOrDefault(key));
        var fromTop = _fromTop > 0 ? _fromTop : _targetTop;
        double Ease(double a, double b) =>
            fromTop > 0 && _targetTop > 0 ? (a / fromTop + (b / _targetTop - a / fromTop) * _t) * ShownTop : b;
        return new(Ease(from.Point, to.Point), Ease(from.Low, to.Low), Ease(from.High, to.High));
    }

    // The axis eases by ratio, so a change of time unit (×1,440 from a minute to a day) moves for the whole ease.
    private double ShownTop =>
        _fromTop <= 0 || _targetTop <= 0 ? _targetTop : _fromTop * Math.Pow(_targetTop / _fromTop, _t);

    private void Animate()
    {
        var next = new Dictionary<string, Span>(StringComparer.Ordinal);
        foreach (var bar in _groups.SelectMany(g => g.Bars))
            if (bar.Rank.Estimate is { Point: var p } e && double.IsFinite(p))
                // A bar without a range has its whisker closed on its top, so one that gains a range opens from there.
                next[bar.Key] =
                    double.IsFinite(e.Low) && double.IsFinite(e.High) && e.High > e.Low
                        ? new Span(p, e.Low, e.High)
                        : new Span(p, p, p);
        // The axis follows the results as they arrive: it eases to the new top, it never stays on the first one.
        var top = _max > 0 ? NiceTop(_max) : ShownTop;
        if (
            top.Equals(_targetTop)
            && next.Count == _target.Count
            && next.All(p => _target.TryGetValue(p.Key, out var v) && v.Equals(p.Value))
        )
            return;
        var shown = next.Keys.Union(_target.Keys).ToDictionary(k => k, Shown, StringComparer.Ordinal);
        _fromTop = _targetTop > 0 ? ShownTop : 0;
        _from.Clear();
        foreach (var (key, value) in shown)
            _from[key] = value;
        _target.Clear();
        foreach (var (key, value) in next)
            _target[key] = value;
        _targetTop = top;
        _t = 0;
        Motion.Ease(
            _canvas,
            0,
            1,
            MotionKind.Fill,
            t =>
            {
                _t = t;
                _canvas.QueueRedraw();
            }
        );
    }

    // The width a bar takes: targets without a result get a narrow slot, the rest share what is left.
    private float SlotOf(BarItem bar) => bar.Empty ? EmptySlot : _slot;

    // What the chart draws left to right: an area, or a run of collapsed neighbours drawn as one stub.
    private List<List<Group>> Columns()
    {
        var columns = new List<List<Group>>();
        foreach (var group in _groups)
        {
            if (group.Collapsed && columns.Count > 0 && columns[^1][0].Collapsed)
                columns[^1].Add(group);
            else
                columns.Add([group]);
        }
        return columns;
    }

    // A stub widens a little for each area it stands for, up to the art it stacks.
    private static float StubWidth(int areas) => StubSlot + Math.Min(areas - 1, StackShown - 1) * StackStep;

    // Slot width from the space available; the canvas widens (and scrolls) only below the minimum slot.
    private void Layout()
    {
        var shown = _groups.Where(g => !g.Collapsed).SelectMany(g => g.Bars).ToList();
        var columns = Columns();
        var fixedWidth =
            shown.Count(b => b.Empty) * EmptySlot
            + columns.Where(c => c[0].Collapsed).Sum(c => StubWidth(c.Count))
            + columns.Count * 4;
        var full = Math.Max(1, shown.Count(b => !b.Empty));
        var plot = Math.Max(0, _scroll.Size.X - AxisWidth - 16 - fixedWidth);
        _slot = Math.Clamp(plot / full, MinSlot, MaxSlot);
        var needed = AxisWidth + 16 + fixedWidth + shown.Count(b => !b.Empty) * _slot;
        var width = needed > _scroll.Size.X + 0.5f ? needed : 0;
        if (!Mathf.IsEqualApprox(_canvas.CustomMinimumSize.X, width))
            _canvas.CustomMinimumSize = new Vector2(width, 0);
        // Under the axis, beside the first band.
        _foldAll.Root.Position = new Vector2(4, Plot().End.Y + PortraitRow + 4);
        _foldAll.Root.Size = new Vector2(AxisWidth - 8, BandRow - 8);
        _hint.Position = new Vector2(_canvas.Size.X - _hint.Size.X - 10, _canvas.Size.Y - LegendRow + 3);
        _canvas.QueueRedraw();
    }

    private Rect2 Plot()
    {
        var size = _canvas.Size;
        return new Rect2(
            AxisWidth,
            TopPad,
            size.X - AxisWidth - 12,
            Math.Max(40, size.Y - TopPad - PortraitRow - BandRow - LegendRow)
        );
    }

    // A round axis top above the largest value.
    private static double NiceTop(double max)
    {
        if (max <= 0 || !double.IsFinite(max))
            return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var step in new[] { 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 })
            if (step * magnitude >= max)
                return step * magnitude;
        return 10 * magnitude;
    }
}
