using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// The Settings view: the header button, how runs simulate, how results rank, and saved runs. Every change is saved
/// at once; one made during a run applies to the next run.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class SettingsView
{
    private const float ColumnWidth = 860;
    private static readonly int[] KeepChoices = [5, 10, 20, 50, 100];

    private readonly HBoxContainer _root;
    private readonly ScrollContainer _scroll;
    private Button _header = null!;
    private Button _spoilers = null!;
    private Button _gearGuide = null!;
    private ScaleRow _scale = null!;
    private Segmented _motion = null!;
    private Label _size = null!;
    private ClearRuns _clear = null!;
    private Dropdown _skin = null!;
    private Label _skinNote = null!;
    private HistoryList _history = null!;
    private Control _savedTitle = null!;
    private RunSettings _runSettings = null!;

    public SettingsView()
    {
        _root = new HBoxContainer
        {
            Name = "Cs2SettingsView",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        // The scroll only as wide as the settings, in the middle, so its bar runs beside them, not at the window's edge.
        _scroll = Ui.Add(
            _root,
            new ScrollContainer
            {
                Name = "Cs2SettingsScroll",
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            }
        );
        var column = Ui.Column(14);
        column.CustomMinimumSize = new Vector2(ColumnWidth, 0);
        _scroll.AddChild(Ui.Margin(column, 0, 0, 12, 0));
        var prefs = PanelState.Prefs;

        WindowSection(column, prefs);
        SimulationSection(column);
        ResultsSection(column, prefs);
        SavedRunsSection(column, prefs);
        ReportSection.Add(column);
        RefreshSize();
    }

    /// <summary>Raised when a kept run is shown from the history, so the window shows Simulate.</summary>
    public event Action? HistoryShown;

    /// <summary>Raised with the run's id when Compare is chosen in the history.</summary>
    public event Action<string>? HistoryCompare;

    /// <summary>Scrolls back to the first setting.</summary>
    public void ShowTop() => _scroll.ScrollVertical = 0;

    /// <summary>Folds the saved runs away: Settings opens short, whatever was open on the last visit.</summary>
    public void FoldHistory() => _history.Fold();

    /// <summary>Scrolls to the saved runs.</summary>
    public void RevealHistory()
    {
        // The list open, and after the view is laid out, the section's title at the top.
        _history.SetExpanded(true);
        if (!_root.IsInsideTree())
            return;
        _root.GetTree().CreateTimer(0.05).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(_root))
                return;
            // Summed local offsets up to the scrolled content: the window's scale makes global rects unreliable here.
            var top = 0f;
            for (Node? node = _savedTitle; node is Control control && node != _scroll; node = node.GetParent())
                top += control.Position.Y;
            _scroll.ScrollVertical = (int)top;
        };
    }

    /// <summary>Describes the history's state, for <c>cs2 ui</c>.</summary>
    public void Describe(System.Text.StringBuilder text)
    {
        _history.Describe(text);
        _clear.Describe(text);
        TieShareRow.Describe(text);
    }

    public Control Root => _root;

    /// <summary>Raised when the header-button setting changes.</summary>
    public event Action<bool>? HeaderChanged;

    /// <summary>Reads again, as the view is shown, the values that change elsewhere.</summary>
    public void Refresh()
    {
        Cs2Check.Set(_header, PanelState.Prefs.ShowHeaderButton);
        Cs2Check.Set(_spoilers, PanelState.Prefs.SpoilerGuard);
        Cs2Check.Set(_gearGuide, PanelState.Prefs.ShowGearGuide);
        _scale.Show();
        MotionRow.Show(_motion);
        _runSettings.Show();
        RefreshSize();
        _history.Refresh();
        _clear.Refresh();
        _tie.Show();
        // mElvUI may have been installed, removed or reloaded since.
        Skins.Refresh(findAgain: true);
        ShowSkin();
    }

    // The skins that can be shown, the one shown selected, and why the chosen one isn't when it isn't.
    private void ShowSkin()
    {
        _skin.Clear();
        _skin.AddItem(Tr.Get("Base Game"));
        if (Skins.MElvUIAvailable)
            _skin.AddItem("mElvUI");
        var skin = Skins.Current;
        _skin.Select(skin.Shown == SkinChoice.MElvUI ? 1 : 0);
        var note = Skins.Note(skin);
        _skinNote.Text = note ?? "";
        _skinNote.Visible = note != null;
    }

    private void SkinChosen(int index)
    {
        Cs2Modal.SetSkin(index == 1 ? SkinChoice.MElvUI : SkinChoice.Base);
        ShowSkin();
    }

    // The fallback note reads as a warning, in the same colour in both skins.
    private static LabelSettings SkinNoteStyle()
    {
        var settings = (LabelSettings)Look.Style(TextStyle.Tiny).Duplicate();
        Look.MarkSkipped(settings);
        settings.FontColor = Look.Risky;
        return settings;
    }

    private static List<Metric> DefaultMetrics()
    {
        var list = RunCache.MetricsOf(RunService.Instance.Current);
        foreach (var key in new[] { Metrics.Kills, Metrics.DeathRate, Metrics.KillTime })
            if (list.All(m => m.Key != key))
                list.Add(Metrics.Describe(key));
        var current = PanelState.Prefs.MetricDefault;
        if (list.All(m => m.Key != current))
            list.Add(Metrics.Describe(current, PanelState.Names()));
        return list;
    }

    private static int Index(int[] choices, int value)
    {
        var index = Array.IndexOf(choices, value);
        return index >= 0 ? index
            : Array.FindIndex(choices, c => c >= value) is var i and >= 0 ? i
            : choices.Length - 1;
    }

    private void RefreshSize()
    {
        var bytes = RunStore.Shared.SizeOnDisk;
        var mb = (bytes / 1048576.0).ToString(bytes == 0 ? "0" : "0.0", Tr.Culture);
        var unsaved = RunService.Instance.Current is { } run && RunStore.Shared.List().All(h => h.Id != run.Id);
        _size.Visible = true;
        _size.Text = unsaved
            ? Tr.Format("Saved with setup, build and seed. {0} MB used. Results on screen not saved.", mb)
            : Tr.Format("Saved with setup, build and seed. {0} MB used.", mb);
    }
}
