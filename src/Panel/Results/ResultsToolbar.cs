using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The results toolbar: the ranked metric with its icon, the time unit, List or Chart, placed by its view; and in a
/// comparison the row that filters which targets show.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class ResultsToolbar
{
    public static readonly TimeUnit[] Units =
    [
        TimeUnit.Kill,
        TimeUnit.Second,
        TimeUnit.Minute,
        TimeUnit.Hour,
        TimeUnit.Day,
    ];

    private readonly HBoxContainer _bar;
    private readonly TextureRect _metricIcon;

    // Each metric icon with mipmaps, made once: the item art is far larger than the icon is drawn, and shrunk without
    // them it blurs.
    private readonly Dictionary<Texture2D, Texture2D> _crisp = [];
    private readonly Dropdown _rankBy;
    private readonly HBoxContainer _unit;
    private readonly Button[] _units;
    private readonly Button _modeList;
    private readonly Button _modeChart;
    private readonly HBoxContainer _compareControls;
    private readonly Button[] _filters;
    private List<Metric> _metrics = [];

    // Null until the first refresh, so the menu is filled even before any run.
    private string? _metricsFor;
    private string? _rankByFor;

    /// <summary>Makes the toolbar row and the comparison's filter, each for its view to place.</summary>
    public ResultsToolbar()
    {
        var bar = _bar = Ui.Row(8, "Cs2Toolbar");
        // No "Rank by" label, to fit the filters' row: the metric's icon leads, and both say it on hover.
        _metricIcon = Ui.Picture(null, 26);
        _metricIcon.TooltipText = Tr.Get("Rank by");
        _metricIcon.MouseFilter = Control.MouseFilterEnum.Stop;
        _metricIcon.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
        bar.AddChild(_metricIcon);
        _rankBy = new Dropdown("Cs2RankBy", Dropdown.Plain) { Ends = 3 };
        _rankBy.Root.TooltipText = Tr.Get("Rank by");
        _rankBy.FixWidth(170);
        _rankBy.ItemSelected += MetricChosen;
        bar.AddChild(_rankBy.Root);
        // The time unit as a row of short buttons: five fixed choices read faster than a list.
        _unit = Ui.Add(bar, Ui.Row(0, "Cs2Unit"));
        var unitGroup = new ButtonGroup();
        _units = [.. Units.Select(unit => UnitButton(unit, unitGroup))];
        foreach (var button in _units)
            _unit.AddChild(button);
        var group = new ButtonGroup();
        // The marks alone, joined as one control, with their names on hover.
        var mode = Ui.Add(bar, Ui.Row(0, "Cs2Mode"));
        _modeList = Look.Narrow(Tab("≡", "Cs2ModeList", group, () => SetMode("list")), 6, 3);
        _modeList.TooltipText = Tr.Get("List");
        _modeChart = Look.Narrow(Tab("▮▮", "Cs2ModeChart", group, () => SetMode("chart")), 6, 3);
        _modeChart.TooltipText = Tr.Get("Chart");
        mode.AddChild(_modeList);
        mode.AddChild(_modeChart);
        // In a comparison: which targets show. The column headers set the order.
        _compareControls = Ui.Row(4, "Cs2CompareControls");
        _compareControls.AddChild(Ui.Label(Tr.Get("Show", "comparison filter"), TextStyle.Label));
        var filterGroup = new ButtonGroup();
        _filters =
        [
            Tab(
                Tr.Get("All", "comparison filter"),
                "Cs2CompareFilterAll",
                filterGroup,
                () => PanelState.SetCompareFilter(CompareFilter.All)
            ),
            Tab(
                Tr.Get("Better"),
                "Cs2CompareFilterBetter",
                filterGroup,
                () => PanelState.SetCompareFilter(CompareFilter.Better)
            ),
            Tab(
                Tr.Get("Worse"),
                "Cs2CompareFilterWorse",
                filterGroup,
                () => PanelState.SetCompareFilter(CompareFilter.Worse)
            ),
        ];
        // Edge to edge, as the unit and mode buttons sit: one choice of three.
        var filters = Ui.Add(_compareControls, Ui.Row(0, "Cs2CompareFilters"));
        foreach (var filter in _filters)
            filters.AddChild(Look.Narrow(filter, 6, 3));
        _compareControls.Visible = false;
    }

    /// <summary>The toolbar row, for its view to place.</summary>
    public Control Root => _bar;

    /// <summary>The comparison's filter row.</summary>
    public Control CompareControls => _compareControls;

    /// <summary>
    /// The metrics offered for results in state <paramref name="state"/>: made by <paramref name="make"/> when the
    /// state changed, else the ones already made.
    /// </summary>
    public List<Metric> MetricsFor(string state, Func<List<Metric>> make)
    {
        if (state != _metricsFor)
        {
            _metricsFor = state;
            _metrics = make();
        }
        return _metrics;
    }

    /// <summary>
    /// Shows the chosen metric, its icon and unit, and the mode. The menu is filled again only when
    /// <paramref name="state"/> changed.
    /// </summary>
    public void Show(
        string state,
        string metric,
        string metricKey,
        Func<string, Texture2D?> icon,
        TimeUnit unit,
        bool chart
    )
    {
        if (_rankByFor != state)
        {
            _rankByFor = state;
            _rankBy.Clear();
            foreach (var m in _metrics)
                _rankBy.AddItem(m.Key == Metrics.MainXp ? Words.Metric(m) : Words.MetricName(m, null), icon(m.Key));
        }
        _rankBy.Select(Math.Max(0, _metrics.FindIndex(m => m.Key == metric)));
        _metricIcon.Texture = Crisp(icon(metricKey));
        _unit.Visible = Metrics.Describe(metricKey).Timed;
        ShowUnit(unit);
        _modeList.SetPressedNoSignal(!chart);
        _modeChart.SetPressedNoSignal(chart);
    }

    private Texture2D? Crisp(Texture2D? texture)
    {
        if (texture == null)
            return null;
        if (_crisp.TryGetValue(texture, out var made))
            return made;
        var image = texture.GetImage();
        if (image == null || image.IsEmpty())
            return texture;
        if (image.IsCompressed())
            image.Decompress();
        image.GenerateMipmaps();
        return _crisp[texture] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>Presses the comparison filter in force.</summary>
    public void ShowFilter(CompareFilter filter)
    {
        for (var i = 0; i < _filters.Length; i++)
            _filters[i].SetPressedNoSignal((int)filter == i);
    }

    private static Button Tab(string text, string name, ButtonGroup group, Action pressed)
    {
        var tab = Ui.Button(text, Look.Tab, name, pressed);
        tab.ToggleMode = true;
        tab.ButtonGroup = group;
        return tab;
    }

    private static void SetMode(string mode)
    {
        if (PanelState.Prefs.ResultsMode == mode)
            return;
        PanelState.Prefs.ResultsMode = mode;
        PanelState.Update();
    }

    private void MetricChosen(int index)
    {
        if (index < 0 || index >= _metrics.Count)
            return;
        PanelState.Prefs.Metric = _metrics[(int)index].Key;
        PanelState.Update();
    }

    private static Button UnitButton(TimeUnit unit, ButtonGroup group)
    {
        var button = Look.Narrow(Tab(Words.PerUnitShort(unit), "Cs2Unit" + unit, group, () => UnitChosen(unit)), 5, 3);
        button.TooltipText = Words.PerUnit(unit);
        return button;
    }

    private static void UnitChosen(TimeUnit unit)
    {
        PanelState.Prefs.TimeUnit = TimeUnits.Name(unit);
        PanelState.Update();
    }

    private void ShowUnit(TimeUnit unit)
    {
        for (var i = 0; i < Units.Length; i++)
            _units[i].SetPressedNoSignal(Units[i] == unit);
    }
}
