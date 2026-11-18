using System.Diagnostics;
using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The layer over CS2's window that shows the About window, the Guide window, the Gear tab's guide or the tour, one at a time. What it
/// shows is in <see cref="PanelState.Help"/>, so a rebuilt window shows it again; closing CS2 clears it and puts back
/// anything the tour changed. It reads the panel; opening About notes this version's changes as seen. Main thread.
/// </summary>
internal static class HelpLayer
{
    private static Control? _layer;
    private static Control? _window;
    private static About? _about;
    private static Explainer? _explainer;
    private static GearGuide? _gearGuide;
    private static Tour? _tour;

    private static HelpState State => PanelState.Help;

    // The control that opened About or the Gear tab's guide; keyboard focus goes back to it when the help closes.
    private static Control? _opener;

    /// <summary>
    /// Makes the layer for a newly built window: the last child of <paramref name="window"/>, over everything.
    /// </summary>
    public static Control Make(Control window)
    {
        _window = window;
        _about = null;
        _explainer = null;
        _gearGuide = null;
        _tour = null;
        _layer = new Control { Name = "Cs2Overlay", MouseFilter = Control.MouseFilterEnum.Ignore };
        return _layer;
    }

    /// <summary>
    /// Opens About from <paramref name="opener"/>, which gets keyboard focus back when the help closes.
    /// </summary>
    public static void OpenAbout(Control? opener)
    {
        _opener = opener;
        if (State.Shown == HelpShown.Tour)
            EndTour();
        State.Shown = HelpShown.About;
        Usage.See();
        Show();
    }

    /// <summary>
    /// Opens the Gear tab's guide from <paramref name="opener"/>, which gets keyboard focus back when it closes.
    /// </summary>
    public static void OpenGearGuide(Control? opener)
    {
        _opener = opener;
        if (State.Shown == HelpShown.Tour)
            EndTour();
        State.Shown = HelpShown.GearGuide;
        Show();
    }

    private static void OpenExplainer()
    {
        State.Shown = HelpShown.Explainer;
        Show();
    }

    /// <summary>Whether the tour or the explainer shows.</summary>
    public static bool Showing => State.Shown != HelpShown.None;

    /// <summary>
    /// Handles Escape: closes the help window shown, or ends the tour; false when none shows, so the window closes.
    /// </summary>
    public static bool Escape()
    {
        switch (State.Shown)
        {
            case HelpShown.About:
            case HelpShown.Explainer:
            case HelpShown.GearGuide:
                CloseWindow();
                return true;
            case HelpShown.Tour:
                EndTour();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Ends the tour (putting back what it changed) and drops About or the explainer when CS2's window closes.
    /// </summary>
    public static void WindowClosed()
    {
        if (State.Shown == HelpShown.Tour)
            Restore();
        State.Shown = HelpShown.None;
        State.Step = 0;
        Clear();
    }

    /// <summary>Runs once a frame while CS2 is open: builds what the state asks for and lays it out.</summary>
    public static void OnFrame()
    {
        if (_layer == null || !GodotObject.IsInstanceValid(_layer) || !_layer.IsInsideTree() || _window == null)
            return;
        switch (State.Shown)
        {
            case HelpShown.About:
                _about ??= MakeAbout();
                _about.Layout();
                break;
            case HelpShown.Explainer:
                _explainer ??= MakeExplainer();
                _explainer.Layout();
                break;
            case HelpShown.GearGuide:
                _gearGuide ??= new GearGuide(_layer, CloseWindow, HideGearGuideButton);
                _gearGuide.Layout();
                break;
            case HelpShown.Tour:
                _tour ??= MakeTour();
                _tour.Update(State.Step);
                break;
        }
    }

    /// <summary>Describes what the help layer shows, for <c>cs2 ui</c>.</summary>
    public static string Describe() =>
        $"help={State.Shown.ToString().ToLowerInvariant()}\ntour_step={State.Step}"
        + $"\ntour_target={(State.Shown == HelpShown.Tour && _tour != null ? _tour.Ringed : "-")}"
        + $"\ntour_side={(State.Shown == HelpShown.Tour && _tour?.Side is { } side ? side.ToString().ToLowerInvariant() : "-")}"
        + $"\nfocus={Focused()}"
        + $"\nabout_dot={(Usage.Dot ? "on" : "off")}"
        + $"\nupdate_notifications={(PanelState.Prefs.UpdateNotifications ? "on" : "off")}"
        + $"\nabout_seen={Usage.Stats.SeenVersion ?? "-"}"
        + $"\nabout_time={Usage.Stats.Session.Seconds.ToString("0", CultureInfo.InvariantCulture)}/"
        + Usage.Stats.AllTime.Seconds.ToString("0", CultureInfo.InvariantCulture)
        + $"\nabout_runs={Usage.Stats.Session.Runs}/{Usage.Stats.AllTime.Runs}"
        + $"\nabout_fights={Usage.Stats.Session.Fights}/{Usage.Stats.AllTime.Fights}"
        + $"\nabout_comparisons={Usage.Stats.Session.Comparisons}/{Usage.Stats.AllTime.Comparisons}";

    private static string Focused() =>
        _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.IsInsideTree()
            ? _layer.GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "none"
            : "-";

    public static void Reset()
    {
        _layer = null;
        _window = null;
        _about = null;
        _explainer = null;
        _gearGuide = null;
        _tour = null;
        State.Shown = HelpShown.None;
        State.Step = 0;
        State.ViewBefore = null;
        _opener = null;
    }

    /// <summary>A dimming rect that takes clicks, so nothing under it is pressed.</summary>
    public static ColorRect Dim(string name) =>
        new()
        {
            Name = name,
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

    // Built at once, in the frame of the click; the time is logged as the window's open time is.
    private static void Show()
    {
        var clock = Stopwatch.StartNew();
        Clear();
        OnFrame();
        Log.Info(
            $"help: {State.Shown.ToString().ToLowerInvariant()} shown in "
                + clock.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)
                + " ms"
        );
    }

    // The opener is the Gear tab's (i): it goes at once, and the preference keeps it gone until Settings shows it again.
    private static void HideGearGuideButton()
    {
        PanelState.Prefs.ShowGearGuide = false;
        PanelState.Save();
        if (_opener != null && GodotObject.IsInstanceValid(_opener))
            _opener.Visible = false;
        _opener = null;
        CloseWindow();
        Cs2Modal.RefreshSettings();
    }

    private static About MakeAbout() => new(_layer!, CloseWindow, OpenExplainer, StartTour);

    private static Explainer MakeExplainer() => new(_layer!, CloseWindow, StartTour);

    private static Tour MakeTour() => new(_layer!, _window!, Next, Back, EndTour);

    private static void CloseWindow()
    {
        State.Shown = HelpShown.None;
        Clear();
        FocusOpener();
    }

    private static void FocusOpener()
    {
        if (_opener != null && GodotObject.IsInstanceValid(_opener) && _opener.IsVisibleInTree())
            _opener.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void StartTour()
    {
        State.Shown = HelpShown.Tour;
        State.Step = 1;
        State.ViewBefore = PanelState.Prefs.View;
        if (State.ViewBefore != Cs2Modal.Simulate)
            Cs2Modal.ShowView(Cs2Modal.Simulate);
        Show();
    }

    private static void Next()
    {
        if (State.Step >= Tour.Steps)
        {
            EndTour();
            return;
        }
        State.Step++;
        StepShown();
    }

    private static void Back()
    {
        if (State.Step <= 1)
            return;
        State.Step--;
        StepShown();
    }

    private static void StepShown()
    {
        var clock = Stopwatch.StartNew();
        OnFrame();
        Log.Info(
            $"help: tour step {State.Step} shown in "
                + clock.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)
                + " ms"
        );
    }

    private static void EndTour()
    {
        Restore();
        State.Shown = HelpShown.None;
        State.Step = 0;
        Clear();
        FocusOpener();
    }

    // Puts back what the tour changed to show its steps. The view is only shown, never chosen by the tour; if
    // anything saved the preferences meanwhile, they are saved again as the player left them.
    private static void Restore()
    {
        if (State.ViewBefore is { } view && view != PanelState.Prefs.View)
        {
            Cs2Modal.ShowView(view);
            PanelState.Save();
        }
        State.ViewBefore = null;
    }

    private static void Clear()
    {
        _about = null;
        _explainer = null;
        _gearGuide = null;
        _tour = null;
        if (_layer == null || !GodotObject.IsInstanceValid(_layer))
            return;
        Ui.Clear(_layer);
    }
}
