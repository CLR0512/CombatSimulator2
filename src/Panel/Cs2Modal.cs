using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Help;
using MelvorMods.CombatSimulator2.Panel.Settings;
using MelvorMods.CombatSimulator2.Panel.SetupTab;
using MelvorMods.CombatSimulator2.Panel.Simulate;
using MelvorMods.CombatSimulator2.Panel.Stats;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// CS2's modal: the frame, title ribbon, Simulate, Setup and Settings tabs and close button, around the views. Core
/// hosts it; it is built once, over several frames (shell first, then each part), and kept while closed.
/// Main thread only.
/// </summary>
internal static partial class Cs2Modal
{
    public const string Simulate = "simulate";
    public const string Setup = "setup";
    public const string Settings = "settings";

    /// <summary>The full stats view; it has no tab of its own and goes back to where it was opened from.</summary>
    public const string Stats = "stats";

    /// <summary>The view id for Settings opened at its saved runs; it has no tab of its own.</summary>
    public const string History = "history";

    private static readonly Queue<Action> _steps = new();
    private static ModModal? _modal;
    private static PanelContainer? _root;
    private static Control? _content;
    private static Button? _tabSimulate;
    private static Button? _tabSetup;
    private static Button? _tabSettings;
    private static SimulateView? _simulate;
    private static SetupTabView? _setupView;
    private static FullStatsView? _statsView;
    private static SettingsView? _settings;
    private static bool _revealHistory;
    private static RefusalNotice? _refusals;
    private static SkinDraw _builtDraw;
    private static bool _rebuilding;

    /// <summary>Milliseconds from the last open until the view was fully built and drawn; -1 before any open.</summary>
    public static double OpenedMs => ModalTiming.OpenedMs;

    public static bool IsOpen => _modal?.IsOpen ?? false;

    /// <summary>True from an open until the view is fully built: slow frames are logged then.</summary>
    public static bool Opening => ModalTiming.Measuring;

    public static SimulateView? SimulateView => _simulate;

    /// <summary>The solid colour behind the window's content in the look drawn.</summary>
    public static Color Backdrop => Look.IsGameLook ? Color.FromHtml("#1d1b18") : Color.FromHtml("#0c0c0c");

    /// <summary>Raised after the window opens (true) or closes (false), whichever way it did.</summary>
    public static event Action<bool>? OpenChanged;

    /// <summary>Raised when the Settings view turns the header's Sim button on or off.</summary>
    public static event Action<bool>? HeaderButtonChanged;

    internal static void Register(ModContext context)
    {
        _modal = context.Modals.Register("Cs2", Build);
        WindowScale.Attach(_modal, context);
        _modal.OpenChanged += Opened;
        // Escape closes the help over the window first, then goes back from a jump to another page, then closes it.
        _modal.Escape = () => HelpLayer.Escape() || Back();
        _modal.OutsideClick = ClickedOutside;
    }

    /// <summary>Opens on <paramref name="view"/>, or the remembered one.</summary>
    public static void Open(string? view = null)
    {
        if (view != null)
            ShowView(view);
        // A skin change while closed is drawn from the start.
        Look.Refresh();
        FollowSkin();
        MotionChoice.Follow();
        _modal?.Open();
    }

    public static void Close() => _modal?.Close();

    /// <summary>Shows the settings as they are now, after a command changed one.</summary>
    public static void RefreshSettings() => _settings?.Refresh();

    /// <summary>Scrolls Settings back to its first setting, from the saved runs or wherever it was left.</summary>
    public static void SettingsFromTop() => _settings?.ShowTop();

    // A click in the dimmed room around the window closes what is open over the window, else the window. The tour
    // and the explainer point at the window from outside it, so a click there with either open does nothing.
    private static void ClickedOutside()
    {
        if (HelpLayer.Showing || Overlays.CloseNewest())
            return;
        Close();
    }

    public static void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    private static void Opened(bool open)
    {
        var started = Stopwatch.StartNew();
        if (open)
            Results.UncheckedStrip.Unfold();
        OpenChanged?.Invoke(open);
        if (_root != null)
            QuietNotifications.Follow(_root, open && PanelState.Prefs.QuietNotifications);
        if (!open)
        {
            if (_simulate is { Ready: true })
                _simulate.Results.Chart.HideHover();
            HelpLayer.WindowClosed();
            _back = null;
            InstantTooltips.Hide();
            RunService.Instance.PanelClosed();
            return;
        }
        RunService.Instance.Warm(); // a run is likely: have the simulator ready for it
        ModalTiming.Start(whileOpen: false);
        _simulate?.Reopened();
        // Settings is refreshed by ApplyView when it is the view shown, and when it is next switched to.
        ApplyView();
        if (_root != null)
            Motion.FadeIn(_root);
        Log.Info(
            $"open: shell {started.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms after build"
        );
    }

    // Drops the built window's parts (Core frees the nodes).
    private static void Forget()
    {
        _steps.Clear();
        Overlays.Clear();
        _root = null;
        _content = null;
        _tabSimulate = null;
        _tabSetup = null;
        _tabSettings = null;
        _simulate = null;
        _setupView = null;
        _statsView = null;
        _settings = null;
        _shownView = null;
    }

    // The shell now; the views one per frame after it. Styles are made fresh for the skin shown.
    private static PanelContainer Build()
    {
        Forget();
        Skins.Refresh();
        Look.ResetStyles();
        _builtDraw = Skins.Current.Draw;
        if (_rebuilding)
        {
            _rebuilding = false;
            ModalTiming.Start(whileOpen: IsOpen);
        }
        _root = Tr.Adopt(Look.Panel(Art.FrameBox, "Cs2Root"));
        // The game look skips mElvUI's skin; it must join before entering the tree, where the skin styles nodes.
        if (Look.IsGameLook)
            _root.AddToGroup(Skins.SkipGroup);
        _root.Theme = WindowTheme();
        // The game's popup frame is see-through inside; a solid backdrop keeps the game from showing through.
        var backdrop = new ColorRect { Name = "Backdrop", MouseFilter = Control.MouseFilterEnum.Ignore };
        void Shade() => backdrop.Color = Backdrop;
        Shade();
        // mElvUI notifies every node when its skin flips. The theme follows at once; a rebuild waits for the frame
        // callback, as nodes can't be added while the notification is going round.
        backdrop.ThemeChanged += () =>
        {
            Look.Refresh();
            if (IsGame(Skins.Current.Draw) && IsGame(_builtDraw) && Skins.Current.Draw != _builtDraw)
                FollowSkin();
            Shade();
        };
        _root.AddChild(backdrop);
        _refusals = new RefusalNotice(_root);
        var column = Ui.Add(_root, Ui.Column(10, "Cs2Body"));

        var top = Ui.Add(column, Ui.Row(6, "Cs2TopBar"));
        var title = Look.Panel(Art.RibbonBox, "Cs2Title");
        title.CustomMinimumSize = new Vector2(360 + RibbonOverhang, 0);
        var titleRow = Ui.Add(title, Ui.Row(8));
        // The ☰ menu on the ribbon itself, before the title.
        titleRow.AddChild(Panel.Simulate.PanelMenu.Button(_root));
        titleRow.AddChild(Ui.Label(Tr.Get("Combat Simulator 2"), TextStyle.Title));
        // Taller by what it reaches up, so its foot stays level with the tabs.
        top.AddChild(Ui.Overhang(title, left: RibbonOverhang, up: FrameOverhang / 2, taller: true));
        var group = new ButtonGroup();
        _tabSimulate = Tab(Tr.Get("Simulate"), "Cs2TabSimulate", group, () => SwitchTo(Simulate));
        _tabSetup = Tab(Tr.Get("Setup"), "Cs2TabSetup", group, () => SwitchTo(Setup));
        _tabSettings = Tab(Tr.Get("Settings"), "Cs2TabSettings", group, () => SwitchTo(Settings));
        top.AddChild(_tabSimulate);
        top.AddChild(_tabSetup);
        top.AddChild(_tabSettings);
        top.AddChild(Ui.Spacer());
        top.AddChild(AboutButton.Make());
        var close = Ui.CloseButton("Cs2Close", Close);
        close.SizeFlagsVertical = Control.SizeFlags.Fill;
        var corner = Ui.Overhang(close, right: FrameOverhang / 2, up: FrameOverhang);
        corner.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(corner);

        // The title bar and tabs stay put; a view larger than the room left scrolls under them, never off the window.
        var scroll = Ui.Add(
            column,
            new ScrollContainer { Name = "Cs2ContentScroll", SizeFlagsVertical = Control.SizeFlags.ExpandFill }
        );
        _content = Ui.Add(
            scroll,
            new MarginContainer
            {
                Name = "Cs2Content",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            }
        );
        _steps.Clear();
        _steps.Enqueue(() =>
        {
            _simulate = new SimulateView(_root);
            _simulate.Root.Visible = false;
            _content.AddChild(_simulate.Root);
            ApplyView();
        });
        if (PanelState.Prefs.View == Settings)
            _steps.Enqueue(EnsureSettings);
        if (PanelState.Prefs.View == Setup)
            _steps.Enqueue(EnsureSetup);
        if (PanelState.Prefs.View == Stats)
            _steps.Enqueue(EnsureStats);
        ApplyView();
        // Last, so the help draws over everything; it shows again what it showed before a rebuild.
        _root.AddChild(HelpLayer.Make(_root));
        return _root;
    }

    // How far the ribbon and the close button reach past the frame's inner margin: just over its border, so the edge
    // reads as alive rather than boxed in. Any further and they float clear of the window.
    private const int FrameOverhang = 6;

    // The ribbon's torn end needs a pixel more to clear the border. The close button reaches out half as far to the
    // side as it does up, so it sticks out past the side and the top by the same amount.
    private const int RibbonOverhang = FrameOverhang + 1;

    private static Button Tab(string text, string name, ButtonGroup group, Action pressed)
    {
        var tab = Ui.Button(text, Look.Tab, name, pressed);
        tab.ToggleMode = true;
        tab.ButtonGroup = group;
        tab.CustomMinimumSize = new Vector2(120, 0);
        return tab;
    }

    /// <summary>Runs once a frame: one build step, then the open view's own work.</summary>
    public static void OnFrame()
    {
        ModalTiming.Frame(IsOpen);
        if (_root == null)
            return;
        // The window goes with the game's screen when the player leaves to the main menu: its parts are dropped at
        // once, so nothing reads a freed view and the next open builds the window again.
        if (!GodotObject.IsInstanceValid(_root))
        {
            Forget();
            return;
        }
        FollowSkin();
        if (_root == null)
            return;
        if (IsOpen)
        {
            PixelDraw.Follow(_root);
            HelpLayer.OnFrame();
            InstantTooltips.OnFrame(_root);
            SideChecker.OnFrame();
            _refusals?.OnFrame();
        }
        // A timestamp, not a stopwatch: this runs every frame with the window closed too, and must allocate nothing.
        var work = Stopwatch.GetTimestamp();
        if (_steps.Count > 0)
        {
            _steps.Dequeue()();
            ModalTiming.Slow("build step", work);
            return;
        }
        if (!IsOpen)
            return;
        if (PanelState.Prefs.View != Settings)
            _simulate?.OnFrame();
        if (PanelState.Prefs.View == Stats && _statsView != null)
            _statsView.Refresh(); // draws again only when the sides' checks or its choices changed
        if (PanelState.Prefs.View == Setup)
            _setupView?.OnFrame();
        ModalTiming.Slow("view frame", work);
        if (
            ModalTiming.Measuring
            && (
                _simulate is { Building: false }
                || PanelState.Prefs.View == Settings && _settings != null
                || PanelState.Prefs.View == Setup && _setupView != null
                || PanelState.Prefs.View == Stats && _statsView != null
            )
        )
            ModalTiming.Finish();
    }

    public static void Reset()
    {
        _steps.Clear();
        _modal = null;
        _root = null;
        _content = null;
        _tabSimulate = null;
        _tabSetup = null;
        _tabSettings = null;
        _simulate = null;
        _setupView = null;
        _statsView = null;
        _settings = null;
        HelpLayer.Reset();
        AboutButton.Reset();
        QuietNotifications.Reset();
        RightColumn.Reset();
        WindowScale.Reset();
        _shownView = null;
        _rebuilding = false;
        HeaderButtonChanged = null;
        OpenChanged = null;
        ModalTiming.Reset();
    }

    /// <summary>Describes the window for <c>cs2 ui</c>, one key=value line per item.</summary>
    public static string Describe(string header)
    {
        var prefs = PanelState.Prefs;
        var run = RunService.Instance.Current;
        var text = new StringBuilder();
        text.Append("open=").Append(IsOpen ? "true" : "false");
        text.Append("\nmotion=").Append(MotionTimes.Name(MotionChoice.Level));
        text.Append("\nscale=").Append(WindowScale.Percent(prefs.Scale));
        text.Append("\nscale_cap=").Append(WindowScale.Percent(WindowScale.Cap));
        text.Append("\nscale_drawn=").Append(WindowScale.Percent(WindowScale.Drawn));
        text.Append("\nscale_base=").Append(WindowScale.Base.ToString("0.00", CultureInfo.InvariantCulture));
        text.Append("\nroom=").Append(CultureInfo.InvariantCulture, $"{WindowScale.Room.X:0}x{WindowScale.Room.Y:0}");
        text.Append("\nview=").Append(prefs.View);
        text.Append("\nmode=").Append(PanelPins.CompareOpen ? "compare" : prefs.ResultsMode);
        Skins.Refresh();
        var skin = Skins.Current;
        text.Append("\nlook=").Append(Look.Kind == LookKind.Game ? "game" : "melvui");
        text.Append("\nskin=").Append(skin.Chosen);
        RefusalNotice.Describe(text);
        Panel.Simulate.PasteDialog.Describe(text);
        text.Append("\ntooltip=").Append(InstantTooltips.Shown);
        text.Append("\nskin_shown=").Append(skin.Shown);
        text.Append("\nskin_reason=").Append(skin.Reason?.English() ?? "none");
        text.Append("\nmelvui_skin=").Append(Skins.SkinOn ? "on" : "off");
        text.Append("\nmetric=").Append(prefs.Metric);
        if (run != null && prefs.Metric == Metrics.MainXp)
            text.Append(" (").Append(Metrics.Resolve(run, prefs.Metric)).Append(')');
        text.Append("\nunit=").Append(TimeUnits.Name(prefs.Unit));
        text.Append("\nselected=").Append(prefs.SelectedTarget is { } s ? Ids.BriefKey(s) : "none");
        text.Append("\npins=").Append(string.Join(",", prefs.Pins.Select(Ids.BriefKey)));
        text.Append("\nfolded-areas=").Append(string.Join(",", prefs.FoldedAreas.Select(Ids.Brief)));
        text.Append("\nbanner=")
            .Append(
                PanelState.Banner switch
                {
                    BannerState.Cancelled c => "cancelled " + c.Count.ToString(CultureInfo.InvariantCulture),
                    BannerState.SetupError e => "setup-error " + e.Reason.English(),
                    BannerState.Failure f => "failure:" + FailureRecord.Name(f.Kind),
                    _ => "none",
                }
            );
        text.Append("\nheader=").Append(header);
        text.Append("\nrun=").Append(run?.Id ?? "none");
        text.Append("\nopened_ms=").Append(OpenedMs.ToString("0", CultureInfo.InvariantCulture));
        text.Append("\nopen_worst_frame_ms=")
            .Append(ModalTiming.WorstFrame.ToString("0.0", CultureInfo.InvariantCulture));
        text.Append(" baseline_p99_ms=").Append(ModalTiming.BaselineP99.ToString("0.0", CultureInfo.InvariantCulture));
        text.Append(" baseline=").Append(ModalTiming.BaselineOf);
        text.Append("\nbuilt=")
            .Append(
                _root == null ? "no"
                : _steps.Count > 0 || _simulate == null ? "building"
                : "yes"
            );
        StateLines.Describe(text);
        text.Append('\n').Append(HelpLayer.Describe());
        text.Append('\n')
            .AppendJoin('\n', Results.UncheckedStrip.Describe(), AboutLinks.Describe(), LanguageRow.Describe());
        if (PanelState.Prefs.View == Stats)
            _statsView?.Describe(text);
        if (PanelState.Prefs.View == Settings)
            _settings?.Describe(text);
        if (PanelState.Prefs.View == Setup)
            _setupView?.Describe(text);
        _simulate?.Describe(text);
        return text.ToString();
    }
}
