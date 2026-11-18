using MelvorMods.CombatSimulator2.Panel.Settings;
using MelvorMods.CombatSimulator2.Panel.SetupTab;
using MelvorMods.CombatSimulator2.Panel.Stats;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel;

// Switching views: Settings, Setup and the full stats view are built when first shown.
internal static partial class Cs2Modal
{
    private static void EnsureSettings()
    {
        if (_settings != null || _content == null)
            return;
        _settings = new SettingsView();
        _settings.HistoryShown += () => Jump(Simulate);
        _settings.HistoryCompare += id =>
        {
            Jump(Simulate);
            _simulate?.CompareAgainst(id);
        };
        _settings.HeaderChanged += on => HeaderButtonChanged?.Invoke(on);
        _settings.Root.Visible = false;
        _content.AddChild(_settings.Root);
        ApplyView();
    }

    private static void EnsureSetup()
    {
        if (_setupView != null || _content == null)
            return;
        _setupView = new SetupTabView();
        _setupView.Root.Visible = false;
        _content.AddChild(_setupView.Root);
        ApplyView();
    }

    private static void EnsureStats()
    {
        if (_statsView != null || _content == null)
            return;
        _statsView = new FullStatsView();
        _statsView.Root.Visible = false;
        _content.AddChild(_statsView.Root);
        ApplyView();
    }

    // A tab picked. The Setup tab picked from Simulate is a step away from it as Edit is: Escape goes back first.
    private static void SwitchTo(string view)
    {
        if (view == Setup && PanelState.Prefs.View == Simulate)
        {
            Jump(view);
            return;
        }
        ShowView(view);
        PanelState.Save();
    }

    // The view a jump left, which Escape returns to once; null when the view was picked with its tab.
    private static string? _back;

    /// <summary>
    /// Shows <paramref name="view"/> from something on the page shown (Edit, All stats, a saved run's Show, the Setup
    /// tab from Simulate), and remembers that page so Escape goes back to it before it closes the window.
    /// </summary>
    public static void Jump(string view)
    {
        var from = PanelState.Prefs.View;
        ShowView(view);
        PanelState.Save();
        if (from != PanelState.Prefs.View)
            _back = from;
    }

    // Escape goes back one step before it closes the window: from picking what to compare, or from the pinned
    // targets side by side, to the results; then, after a jump, to the page it left, once. False when there is none.
    private static bool Back()
    {
        if (PanelState.Prefs.View == Simulate && PanelState.PickingBaseline)
        {
            PanelState.PickBaseline(false);
            return true;
        }
        if (PanelState.Prefs.View == Simulate && PanelPins.CompareOpen)
        {
            PanelPins.SetCompare(false);
            return true;
        }
        if (_back is not { } view)
            return false;
        ShowView(view);
        PanelState.Save();
        return true;
    }

    /// <summary>Sets the view to show (and remember), forgetting any jump Escape would return from.</summary>
    public static void ShowView(string view)
    {
        _back = null;
        _revealHistory = view == History;
        PanelState.Prefs.View =
            view is Settings or History ? Settings
            : view is Setup or Stats ? view
            : Simulate;
        if (PanelState.Prefs.View == Settings && _content != null && _settings == null)
            _steps.Enqueue(EnsureSettings);
        if (PanelState.Prefs.View == Setup && _content != null && _setupView == null)
            _steps.Enqueue(EnsureSetup);
        if (PanelState.Prefs.View == Stats && _content != null && _statsView == null)
            _steps.Enqueue(EnsureStats);
        ApplyView();
    }

    private static string? _shownView;

    private static void ApplyView()
    {
        var view = PanelState.Prefs.View;
        var settings = view == Settings;
        var setup = view == Setup;
        var stats = view == Stats;
        var simulate = !settings && !setup && !stats;
        var switched = _shownView != view;
        if (_shownView != null && switched)
        {
            if (settings && _settings != null)
            {
                Motion.FadeIn(_settings.Root);
                _settings.FoldHistory();
            }
            else if (setup && _setupView != null)
                Motion.FadeIn(_setupView.Root);
            else if (stats && _statsView != null)
                Motion.FadeIn(_statsView.Root);
            else if (simulate && _simulate != null)
                Motion.FadeIn(_simulate.Root);
        }
        _shownView = view;
        // The stats view has no tab: the tab it was opened from stays pressed.
        _tabSimulate?.SetPressedNoSignal(simulate || stats && FullStatsView.From == Simulate);
        _tabSetup?.SetPressedNoSignal(setup || stats && FullStatsView.From == Setup);
        _tabSettings?.SetPressedNoSignal(settings);
        if (_simulate != null)
            _simulate.Root.Visible = simulate;
        if (_setupView != null)
            _setupView.Root.Visible = setup;
        if (_statsView != null)
        {
            _statsView.Root.Visible = stats;
            if (stats)
                _statsView.Refresh();
        }
        if (_settings != null)
        {
            _settings.Root.Visible = settings;
            if (settings)
            {
                _settings.Refresh();
                if (_revealHistory)
                {
                    _revealHistory = false;
                    _settings.RevealHistory();
                }
            }
        }
    }
}
