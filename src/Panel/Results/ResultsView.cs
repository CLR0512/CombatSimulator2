using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Compare;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The results area: "Rank by", unit, List or Chart, and progress on top; the cancelled or setup-error banner; the
/// empty state before any run; the ranked list or the chart, narrowed to the targets the filter shows; the ignored
/// targets; and the compare tray of pinned targets underneath.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class ResultsView
{
    private readonly VBoxContainer _root;
    private readonly ResultsToolbar _toolbar;
    private readonly VBoxContainer _bannerHolder;
    private readonly Label _dimNote;
    private readonly EmptyState _emptyState;
    private readonly RankList _list;
    private readonly Chart _chart;
    private readonly VBoxContainer _warnings;
    private readonly ResultsBanners _banners;
    private readonly UncheckedStrip _unchecked;
    private readonly CompareList _compareList;
    private readonly PanelContainer _compareChartPanel;
    private readonly CompareChart _compareChart;
    private readonly Label _message;
    private readonly PinTray _tray;
    private readonly NarrowLine _narrow = new();
    private readonly HBoxContainer _foot;
    private readonly PanelContainer _bar;
    private string? _shownMode;
    private string? _heldComparison;
    private List<string> _heldCompare = [];

    // The order shown during the active run, so rows don't jump as results come in; it sorts once when final.
    private string? _heldRun;
    private List<string> _held = [];

    public ResultsView(Control hoverOwner)
    {
        _root = Ui.Column(8, "Cs2Results");
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        _toolbar = new ResultsToolbar();
        _unchecked = new UncheckedStrip(_root, _toolbar.Root);

        _bannerHolder = Ui.Add(_root, Ui.Column(0));
        _dimNote = Ui.Label("", TextStyle.Small, "Cs2DimNote");
        _dimNote.Visible = false;
        _root.AddChild(_dimNote);

        _emptyState = new EmptyState();
        _root.AddChild(_emptyState.Root);

        _list = new RankList();
        _list.Message += ShowMessage;
        _root.AddChild(_list.Root);
        var chartPanel = Look.Panel(Art.RowBox, "Cs2ChartPanel");
        chartPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _chart = new Chart(hoverOwner);
        _chart.Message += ShowMessage;
        _narrow.Message += ShowMessage;
        chartPanel.AddChild(_chart.Root);
        _root.AddChild(chartPanel);

        _warnings = Ui.Add(_root, Ui.Column(4, "Cs2CompareWarnings"));
        _banners = new ResultsBanners(_bannerHolder, _warnings);
        _compareList = new CompareList();
        _compareList.Root.Visible = false;
        _root.AddChild(_compareList.Root);
        _compareChartPanel = Look.Panel(Art.RowBox, "Cs2CompareChartPanel");
        _compareChartPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _compareChart = new CompareChart();
        _compareChartPanel.AddChild(_compareChart.Root);
        _compareChartPanel.Visible = false;
        _root.AddChild(_compareChartPanel);

        // Only while it says something, so the chart reaches down to the run control.
        _message = Ui.Label("", TextStyle.Small, "Cs2Message");
        _message.Visible = false;
        _root.AddChild(_message);

        // Under the results, beside the run control the view puts there: one bar level with its button, the ignored
        // targets at its left and the pins at its right, so neither takes height from the results.
        _foot = Ui.Add(_root, Ui.Row(10, "Cs2ResultsFoot"));
        // At the right whether the pins show or not (a comparison has none).
        _foot.Alignment = BoxContainer.AlignmentMode.End;
        var left = Ui.Add(_foot, Ui.Column(8));
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.Alignment = BoxContainer.AlignmentMode.End;
        // The list's note there too, so the list's rows reach down to it.
        // With the comparison's filter at its end, in room the run control leaves beside it, not a row of its own.
        var notes = Ui.Add(left, Ui.Row(12));
        notes.AddChild(_compareList.Note);
        _compareList.Note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _compareList.Note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        notes.AddChild(_toolbar.CompareControls);
        left.AddChild(_narrow.Root);
        _bar = Ui.Add(left, Look.Panel(Art.RowBox, "Cs2ResultsBar"));
        // No padding above and below: the bar is held as tall as the run button, and its buttons fit inside.
        var box = (StyleBox)Look.Box(Art.RowBox).Duplicate();
        box.ContentMarginTop = box.ContentMarginBottom = 0;
        PixelBox.Panel(_bar, box);
        // The row box's own margin leaves the ends against its edges: the pins get some room.
        _tray = new PinTray();
        _tray.Root.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _bar.AddChild(Ui.Margin(_tray.Root, 12, 0, 8, 0));
        // Shown while there are pins to show.
        _tray.Root.VisibilityChanged += () => _bar.Visible = _tray.Root.Visible;
        _bar.Visible = _tray.Root.Visible;
    }

    public Control Root => _root;

    /// <summary>Puts the run control under the results, right of the bar, the bar as tall as its button.</summary>
    public void AddRunControl(Control control, Control button)
    {
        control.CustomMinimumSize = new Vector2(Details.Width, 0);
        // At the foot's bottom, its button level with the bar, however tall the notes beside it make the foot.
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        _foot.AddChild(control);
        button.Resized += () => _bar.CustomMinimumSize = new Vector2(0, button.Size.Y);
    }

    /// <summary>
    /// The toolbar row: rank, unit, List or Chart. The view puts it in the filters' row, so the chart and list get the
    /// height of a second row.
    /// </summary>
    public Control Toolbar => _toolbar.Root;

    public RankList List => _list;

    private RankTable HeldOrder(Run run, RankTable table)
    {
        if (_heldRun != run.Id)
        {
            _heldRun = run.Id;
            _held = [];
        }
        var shown = Ranking.Hold(table, _held, final: run.IsFinal);
        _held = [.. shown.Rows.Select(r => r.Key)];
        return shown;
    }

    public Chart Chart => _chart;

    public CompareList CompareList => _compareList;

    public CompareChart CompareChart => _compareChart;

    /// <summary>Raised when the comparison's banner asks for another saved run.</summary>
    public event Action? PickAnotherBaseline
    {
        add => _banners.PickAnotherBaseline += value;
        remove => _banners.PickAnotherBaseline -= value;
    }

    public event Action? SimulateFighting
    {
        add => _emptyState.SimulateFighting += value;
        remove => _emptyState.SimulateFighting -= value;
    }

    public event Action? SimulateSelected
    {
        add => _emptyState.SimulateSelected += value;
        remove => _emptyState.SimulateSelected -= value;
    }

    /// <summary>Raised by "Simulate the N again" with the keys that were cancelled.</summary>
    public event Action<IReadOnlyList<string>>? SimulateAgain
    {
        add => _banners.SimulateAgain += value;
        remove => _banners.SimulateAgain -= value;
    }

    public event Action? UseCurrentSetup
    {
        add => _banners.UseCurrentSetup += value;
        remove => _banners.UseCurrentSetup -= value;
    }

    /// <summary>
    /// Raised by the host-error banner's button, to simulate these targets again in the game's process.
    /// </summary>
    public event Action<IReadOnlyList<string>>? UseGameProcess
    {
        add => _banners.UseGameProcess += value;
        remove => _banners.UseGameProcess -= value;
    }

    public void ShowMessage(string text)
    {
        _message.Text = text;
        _message.Visible = text.Length > 0;
    }

    // The rows the filter shows, the ignored ones with them, greyed in place in the list and crossed out in the chart.
    // Every row while targets aren't checked yet.
    private static HashSet<string>? Kept(TargetSelection? selection) =>
        selection == null ? null : [.. selection.Shown.Select(t => t.Key)];

    /// <summary>Redraws from the run and preferences. Returns whether rows are still being made.</summary>
    public bool Refresh(Run? run, int selected, bool setupError, TargetSelection? selection)
    {
        ShowComparisonParts(false);
        var prefs = PanelState.Prefs;
        var service = RunService.Instance;
        var game = LiveGame.Find();

        // Rank by: keep the choice while the run lacks it (a new run will have it), but show what's ranked.
        // The metric list and its menu change only as results arrive.
        var state = RunCache.Stamp(run);
        var metrics = _toolbar.MetricsFor(state, () => RunCache.Metrics(run));
        if (metrics.All(m => m.Key != prefs.Metric) && run is { IsFinal: true } && metrics.Count > 1)
            prefs.Metric = prefs.MetricDefault;
        var metricKey = run != null ? Metrics.Resolve(run, prefs.Metric) : prefs.Metric;
        var chart = prefs.ResultsMode == "chart";
        _toolbar.Show(
            state,
            prefs.Metric,
            metricKey,
            key => game != null ? Art.Metric(game, run != null ? Metrics.Resolve(run, key) : key) : null,
            prefs.Unit,
            chart
        );

        _unchecked.Show(run?.Checked == false);
        _banners.Refresh();
        _dimNote.Visible = setupError && run != null;
        _dimNote.Text =
            run == null ? ""
            : run.Setup.Loadout == Setup.Current ? Tr.Get("Showing the last results, for the character as is.")
            : Tr.Format("Showing the last results, for {0}.", Words.Setup(run.Setup));

        var empty = run == null;
        _emptyState.Show(empty, selected, game != null, spec => Art.Target(game!, spec));
        if (_shownMode != null && _shownMode != prefs.ResultsMode)
            Motion.FadeIn(chart ? _chart.Root.GetParent<Control>() : _list.Root);
        _shownMode = prefs.ResultsMode;
        _list.Root.Visible = !empty && !chart;
        _chart.Root.GetParent<Control>().Visible = !empty && chart;
        var pending = false;
        if (run != null)
        {
            var table = HeldOrder(run, RunCache.Rank(run, prefs.Metric, prefs.Unit));
            if (Kept(selection) is { } kept)
                table = table.Narrow(kept);
            if (chart)
            {
                _chart.Show(run, table, prefs.Unit);
            }
            else
            {
                _list.Show(run, table);
                pending = _list.Pending;
                _chart.HideHover();
            }
        }
        else
        {
            _list.Clear();
        }
        _root.Modulate = Colors.White with { A = setupError && run != null ? 0.7f : 1f };

        _tray.Refresh(run);
        _narrow.Refresh(run?.Targets.Count ?? 0);
        return pending;
    }

    private void ShowComparisonParts(bool comparing)
    {
        _toolbar.CompareControls.Visible = comparing;
        _warnings.Visible = comparing;
        if (!comparing)
        {
            _bannerHolder.Visible = true;
            _compareList.Root.Visible = false;
            _compareList.Note.Visible = false;
            _compareChartPanel.Visible = false;
            _compareChart.HideHover();
        }
    }

    /// <summary>
    /// Redraws a comparison in place of a single run's results. Returns whether rows are still being made.
    /// </summary>
    public bool RefreshComparison(ComparedRuns comparison, TargetSelection? selection)
    {
        ShowComparisonParts(true);
        var prefs = PanelState.Prefs;
        var game = LiveGame.Find();
        var sides = comparison.BStarted ? comparison.B : comparison.A;
        var state = RunCache.Stamp(comparison.A) + "|" + RunCache.Stamp(sides);
        _toolbar.MetricsFor(state, () => RunCache.MetricsOf(comparison.A, sides));
        var metricKey = Metrics.Resolve(comparison.A, prefs.Metric);
        var chart = prefs.ResultsMode == "chart";
        _toolbar.Show(
            state,
            prefs.Metric,
            metricKey,
            key => game != null ? Art.Metric(game, Metrics.Resolve(comparison.A, key)) : null,
            prefs.Unit,
            chart
        );
        _toolbar.ShowFilter(PanelState.CompareFilter);

        _banners.RefreshWarnings(comparison);
        // A failure shows above the comparison's warnings; the other banners belong to a single run.
        _bannerHolder.Visible = PanelState.Banner is BannerState.Failure;
        _unchecked.Show(comparison.A.Checked == false || comparison.B.Checked == false);
        _banners.Refresh();
        _dimNote.Visible = false;
        _emptyState.Root.Visible = false;
        _list.Root.Visible = false;
        _chart.Root.GetParent<Control>().Visible = false;
        _chart.HideHover();
        _tray.Root.Visible = false;
        // A comparison's targets are counted once: side A's.
        _narrow.Refresh(comparison.A.Targets.Count);
        _root.Modulate = Colors.White;

        var table = CompareTables.Of(
            comparison.A,
            comparison.B,
            prefs.Metric,
            prefs.Unit,
            PanelState.Prices(),
            PanelState.CompareOrder,
            PanelState.CompareFilter
        );
        // Rows keep their place while either side runs, and sort once both are final.
        if (_heldComparison != comparison.Record.Id + "|" + comparison.B.Id)
        {
            _heldComparison = comparison.Record.Id + "|" + comparison.B.Id;
            _heldCompare = [];
        }
        table = table.Hold(_heldCompare, final: comparison.IsFinal);
        _heldCompare = [.. table.Rows.Select(r => r.Key)];
        // As for a single run: ignored pairs stay, greyed.
        if (Kept(selection) is { } kept)
            table = table with { Rows = [.. table.Rows.Where(r => kept.Contains(r.Key))] };
        _compareList.Root.Visible = !chart;
        _compareChartPanel.Visible = chart;
        if (chart)
        {
            // The list's note serves the chart too: it sits in the foot either way, not in a row of its own.
            _compareList.Note.Visible = comparison.Record.Targets.Count > 1;
            _compareChart.Show(comparison, table);
            return false;
        }
        _compareList.Show(comparison, table);
        return _compareList.Pending;
    }

    /// <summary>Redraws the selection and pins only, which is cheaper than a full refresh.</summary>
    public void RefreshMarks(Run? run)
    {
        _list.RefreshMarks();
        _compareList.RefreshMarks();
        if (PanelState.Prefs.ResultsMode == "chart" && run != null)
            _chart.Root.QueueRedraw();
        _tray.Refresh(run);
    }
}
