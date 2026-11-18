using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// What depends on which run is on screen: the pins it has and the ones waiting for a run that has them, the
/// side-by-side view of the pinned targets, and the wider run it replaced. Main thread only.
/// </summary>
internal static class PanelPins
{
    private static Preferences Prefs => PanelState.Prefs;

    // The run these were last worked out for.
    private static Run? _shown;
    private static IReadOnlyList<string> _shownPins = [];
    private static IReadOnlyList<string> _waitingPins = [];
    private static EarlierResults? _earlier;

    /// <summary>Whether the pinned targets side by side replace the results.</summary>
    public static bool CompareOpen { get; private set; }

    /// <summary>The pins on targets the shown run has, in pin order: the ones every view draws.</summary>
    public static IReadOnlyList<string> ShownPins
    {
        get
        {
            FollowShownRun();
            return _shownPins;
        }
    }

    /// <summary>
    /// The pins on targets the shown run doesn't have. They stay pinned and show again with a run that has them.
    /// </summary>
    public static IReadOnlyList<string> WaitingPins
    {
        get
        {
            FollowShownRun();
            return _waitingPins;
        }
    }

    /// <summary>The wider run that the results on screen replaced this session, or null.</summary>
    public static EarlierResults? Earlier
    {
        get
        {
            FollowShownRun();
            return _earlier;
        }
    }

    /// <summary>Whether <see cref="Earlier"/>'s run can still be shown: it is on disk.</summary>
    public static bool EarlierSaved => Earlier is { } earlier && RunStore.Shared.Has(earlier.RunId);

    // Runs start and saved runs are shown from the panel and from cs2 alike, so the run on screen is looked at
    // whenever one of these is read, and worked through once per run.
    private static void FollowShownRun()
    {
        var run = RunService.Instance.Current;
        if (ReferenceEquals(run, _shown))
            return;
        var replaced = _shown;
        _shown = run;
        if (run == null)
            _earlier = null;
        else if (replaced?.Id != run.Id)
            _earlier = EarlierResults.After(replaced, run.Id, run.Targets.Count, _earlier, RunStore.Shared.Has);
        SortPins();
        // A run that is neither side of the comparison on screen is a single run's results: show them. A kept
        // comparison opened again has no side B to end, so nothing else would leave it.
        if (
            run != null
            && PanelState.ComparisonShown
            && RunService.Instance.Comparison is { } comparison
            && run.Id != comparison.A.Id
            && run.Id != comparison.B.Id
        )
            PanelState.ShowComparison(false);
    }

    private static void SortPins()
    {
        var run = _shown;
        _shownPins = [.. Prefs.Pins.Where(k => run?.Results.ContainsKey(k) == true)];
        _waitingPins = [.. Prefs.Pins.Except(_shownPins, StringComparer.Ordinal)];
        if (_shownPins.Count < 2)
            CompareOpen = false;
    }

    /// <summary>
    /// Follows every change of the run on screen from now on, also while the panel is closed and nothing reads the
    /// pins: a run shown and replaced between two reads would otherwise never be seen as the earlier one.
    /// </summary>
    public static void Watch() => RunService.Instance.Changed += FollowShownRun;

    /// <summary>The mod loading or unloading: nothing has been on screen yet.</summary>
    public static void ForgetShownRun()
    {
        _shown = null;
        _earlier = null;
        SortPins();
    }

    /// <summary>Opens or closes the side-by-side view; it needs two pins the shown run has.</summary>
    public static void SetCompare(bool open)
    {
        if (open && ShownPins.Count < 2)
            open = false;
        if (CompareOpen == open)
            return;
        CompareOpen = open;
        PanelState.Notify();
    }

    /// <summary>
    /// Why a pin is refused: there are as many as can be compared, the waiting ones counted and named as such.
    /// </summary>
    public static Note PinsFull =>
        WaitingPins.Count > 0
            ? Note.Of("pins full ({0}, {1} waiting): unpin one or clear", Preferences.MaxPins, WaitingPins.Count)
            : Note.Of("pins full ({0}): unpin one first", Preferences.MaxPins);

    /// <summary>Pins or unpins a target. A fourth pin is refused with the reason.</summary>
    public static void TogglePin(string key)
    {
        var pins = Prefs.Pins;
        if (!pins.Remove(key))
        {
            if (pins.Count >= Preferences.MaxPins)
                throw new CommandException(PinsFull.English());
            pins.Add(key);
        }
        FollowShownRun();
        SortPins();
        PanelState.Update();
    }

    /// <summary>Unpins every target, the waiting ones too.</summary>
    public static void ClearPins()
    {
        Prefs.Pins.Clear();
        SortPins();
        PanelState.Update();
    }

    /// <summary>
    /// A run started. Pins on targets it doesn't have are kept: they wait for a run that has them.
    /// </summary>
    public static void RunStarted()
    {
        FollowShownRun();
        PanelState.SetBanner(null);
    }
}
