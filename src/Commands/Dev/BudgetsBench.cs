using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Dev;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// Measures every budget the running game can show, as one sequence stepped once a frame on the main thread: the
/// closed panel, each thing a player does to the window (in both looks), smoothness, and the host's memory. Each
/// step ends as a measurement or as "not measured" with the reason. It drives CS2's own window and host, saves no
/// run, and puts back every setting it changed.
/// </summary>
internal static class BudgetsBench
{
    private static IEnumerator<object?>? _script;
    private static readonly Dictionary<string, BudgetLine> _lines = new(StringComparer.Ordinal);
    private static IReadOnlyList<Budget> _budgets = [];
    private static Action? _restore;
    private static string _state = "idle";
    private static string _step = "";
    private static int _stepIndex;
    private static int _stepCount;
    private static int _repeats;
    private static double _seconds;
    private static int _next;

    public static string Id { get; private set; } = "none";

    public static bool Running => _script != null;

    /// <summary>While set, a run that ends is shown but not saved or indexed: a bench must not fill the saved runs.</summary>
    public static bool SavesNothing { get; private set; }

    /// <summary>Starts the sequence, or says why it can't. Main thread.</summary>
    public static void Start(IReadOnlyList<Budget> budgets, int repeats, double seconds, IReadOnlyList<string> looks)
    {
        MainThread.Assert("BudgetsBench.Start");
        if (Running || Bench.Running)
            throw new CommandException("a bench is still running (cs2dev bench result, cs2dev bench stop)");
        if (RunService.Instance.IsActive)
            throw new CommandException("a run is active: cancel it first");
        if (Godot.DisplayServer.WindowGetMode() == Godot.DisplayServer.WindowMode.Minimized)
            throw new CommandException("the game's window is minimised: frames can't be measured");
        LiveGame.Require();
        Id = $"bench-{++_next}";
        _budgets = budgets;
        _repeats = repeats;
        _seconds = seconds;
        _lines.Clear();
        _stepIndex = 0;
        _stepCount = StepCount(looks.Count);
        _state = "running";
        _restore = Restorer();
        SavesNothing = true;
        _script = Sequence(looks).GetEnumerator();
        Log.Info($"{Id}: {budgets.Count} budgets, {repeats} takes of {seconds:0} s, looks {string.Join(",", looks)}");
    }

    /// <summary>Frame callback, after the window's own: one step of the sequence.</summary>
    public static void OnFrame()
    {
        if (_script == null)
            return;
        try
        {
            if (!_script.MoveNext())
                End("done");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"{Id} failed at {_step}", e);
            End($"failed at {_step}: {e.Message}");
        }
    }

    public static void Stop()
    {
        if (Running)
            End("stopped");
    }

    private static void End(string state)
    {
        _script = null;
        _state = state;
        InteractionTiming.Stop();
        Bench.Stop();
        _restore?.Invoke();
        _restore = null;
        SavesNothing = false;
        Log.Info(Result());
    }

    // Everything the sequence changes, as it was when it started.
    private static Action Restorer()
    {
        var prefs = PanelState.Prefs;
        var (skin, view, open) = (prefs.Skin, prefs.View, Cs2Modal.IsOpen);
        var host = HostClient.Instance;
        var reserve = host.ReserveBytes;
        var (release, stop) = (RunService.ReleaseDelay, host.Idle.ClosedLimit);
        var shown = RunService.Instance.Current?.Id;
        return () =>
        {
            RunService.SetReleaseDelay(release);
            host.Idle.ClosedLimit = stop;
            PanelDrive.Restore();
            if (PanelState.Prefs.Skin != skin)
                Cs2Modal.SetSkin(skin);
            host.SetReserveBytes(reserve);
            var service = RunService.Instance;
            if (service.IsActive)
                service.Cancel(Note.Text("cancelled: the bench ended"));
            // A measuring run was shown but not saved: back to the run that was on screen, if it is still kept.
            if (service.Current?.Id != shown && shown != null && RunFinder.Saved?.Invoke(shown) is { } saved)
                service.Show(saved);
            Cs2Modal.ShowView(view);
            if (open)
                Cs2Modal.Open();
            else
                Cs2Modal.Close();
        };
    }

    /// <summary>
    /// Progress while it runs (<c>bench-n step (i/count)</c>), then the header and a line per budget.
    /// </summary>
    public static string Result()
    {
        if (Running)
            return $"{Id} {_step} ({_stepIndex}/{_stepCount})";
        if (_lines.Count == 0)
            return $"{Id} {_state}";
        var lines = _budgets.Select(b => _lines.GetValueOrDefault(b.Name) ?? NotReached(b)).ToList();
        var text = BudgetsReport.Render(Id, lines, LiveGame.GameBuild, DateTime.Now);
        return _state == "done" ? text : $"{text}\n{_state}";
    }

    /// <summary>The seconds each frame window lasts, and how many takes each budget gets.</summary>
    internal static double Seconds => _seconds;

    internal static int Repeats => _repeats;

    internal static Budget Budget(string name) => _budgets.First(b => b.Name == name);

    internal static void Set(BudgetLine line) => _lines[line.Name] = line;

    /// <summary>A budget taken in both looks keeps the worse of its two lines.</summary>
    internal static void Merge(BudgetLine line) =>
        _lines[line.Name] = _lines.TryGetValue(line.Name, out var other) ? BudgetsReport.Worse(other, line) : line;

    private static NotMeasuredLine NotReached(Budget budget) => new(budget.Name, $"the bench {_state} first");

    internal static bool Wants(string name) => _budgets.Any(b => b.Name == name);

    internal static bool WantsGroup(string group) => _budgets.Any(b => b.Group == group && b.Script == null);

    internal static void Step(string step)
    {
        _step = step;
        _stepIndex++;
        if (_stepIndex > _stepCount)
            _stepCount = _stepIndex;
    }

    private static int StepCount(int looks)
    {
        var windows = 0;
        if (WantsGroup(Budgets.Closed) || WantsGroup(Budgets.Panel))
            windows += 1;
        if (WantsGroup(Budgets.Panel))
            windows += 1 + BudgetsPanelSteps.Count();
        return (windows * _repeats + (WantsGroup(Budgets.Panel) ? 1 : 0)) * looks
            + (WantsGroup(Budgets.Smoothness) ? 3 : 0)
            + (WantsGroup(Budgets.Memory) ? 5 * _repeats : 0);
    }

    private static IEnumerable<object?> Sequence(IReadOnlyList<string> looks)
    {
        foreach (var budget in _budgets.Where(b => b.Script != null))
            _lines[budget.Name] = BudgetsReport.Elsewhere(budget);
        if (WantsGroup(Budgets.Closed) || WantsGroup(Budgets.Panel))
        {
            foreach (var look in looks)
            foreach (var _ in BudgetsPanelSteps.Run(look))
                yield return null;
            PanelDrive.Restore();
        }
        if (WantsGroup(Budgets.Smoothness))
            foreach (var _ in BudgetsHostSteps.Smoothness())
                yield return null;
        if (WantsGroup(Budgets.Memory))
            foreach (var _ in BudgetsHostSteps.Memory())
                yield return null;
    }

    // ---- shared pieces of the steps

    internal static IEnumerable<object?> Wait(double seconds)
    {
        var until = Environment.TickCount64 + (long)(seconds * 1000);
        while (Environment.TickCount64 < until)
            yield return null;
    }

    /// <summary>Waits until <paramref name="done"/>, at most <paramref name="timeout"/> seconds.</summary>
    internal static IEnumerable<object?> Until(Func<bool> done, double timeout)
    {
        var until = Environment.TickCount64 + (long)(timeout * 1000);
        while (!done() && Environment.TickCount64 < until)
            yield return null;
    }

    // The window as asked and built, then a moment for its fades and deferred layout.
    internal static IEnumerable<object?> Settle(double after = 0.5)
    {
        yield return null;
        foreach (var _ in Until(() => Cs2Modal.Settled, 10))
            yield return null;
        foreach (var _ in Wait(after))
            yield return null;
    }

    internal static IEnumerable<object?> Window(double seconds, BenchDrive? drive, Action<WindowStats> done)
    {
        var window = new FrameWindow();
        while (window.Watch.Elapsed.TotalSeconds < seconds)
        {
            yield return null;
            window.Sample();
            drive?.Frame(window.Watch.Elapsed.TotalSeconds);
        }
        drive?.Finish();
        done(window.Stats());
    }

    /// <summary>Starts <c>run all</c> with seed 1, fresh, in the host; the run is shown but never saved.</summary>
    internal static void StartRun()
    {
        var game = LiveGame.Require();
        RunService.Instance.Start(
            new RunRequest(
                RunArgs.SetupOf(null, false),
                [.. Targets.All(game)],
                1000,
                1,
                Engine.DefaultCopies.Now,
                RunService.InHost,
                Fresh: true
            )
        );
    }

    internal static bool LookShown(string look) =>
        look == SkinChoice.MElvUI ? Skins.Current.IsMElvUILook : !Skins.Current.IsMElvUILook;
}
