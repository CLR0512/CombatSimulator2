namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>One window's smoothness: frame rate, frame times, tick lag, GC pause time and collections.</summary>
/// <param name="FrameTimes">Every frame's time in ms, sorted.</param>
internal sealed record WindowStats(
    double Fps,
    double FrameP50,
    double FrameP99,
    double FrameMax,
    double LagMax,
    double LagEnd,
    double GcPauseMs,
    int Frames,
    int Gen0,
    int Gen1,
    int Gen2,
    IReadOnlyList<double> FrameTimes
)
{
    public string Collections => $"gen0 {Gen0} gen1 {Gen1} gen2 {Gen2}";
}

/// <summary>
/// One limit: the run window's value, the control window's, the bound it must stay within, and the verdict.
/// </summary>
internal sealed record LimitResult(string Name, double Value, double Control, double Bound, bool Pass);

/// <summary>
/// Whether a run window kept the live game as smooth as a control window with nothing simulating. Each limit allows
/// for the game's own noise, measured in the control window. Being exactly at a bound passes.
/// </summary>
internal static class BenchLimits
{
    public static IReadOnlyList<LimitResult> Evaluate(WindowStats control, WindowStats run)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(run);
        var fpsBound = 0.9 * control.Fps;
        var lagBound = Math.Max(3, control.LagMax + 1);
        var longMs = 2 * control.FrameP99;
        var longControl = Long(control, longMs);
        var gcBound = control.GcPauseMs + Math.Max(0.1 * control.GcPauseMs, 100);
        return
        [
            new("fps", run.Fps, control.Fps, fpsBound, run.Fps >= fpsBound - 1e-9),
            new("tick-lag", run.LagMax, control.LagMax, lagBound, run.LagMax <= lagBound + 1e-9),
            new(
                "lost",
                Lost(run, control),
                Lost(control, control),
                Lost(control, control),
                Lost(run, control) <= Lost(control, control)
            ),
            new("long", Long(run, longMs), longControl, longControl + 1, Long(run, longMs) <= longControl + 1),
            new("gc-pause", run.GcPauseMs, control.GcPauseMs, gcBound, run.GcPauseMs <= gcBound + 1e-9),
        ];
    }

    public static bool Pass(IEnumerable<LimitResult> results) => results.All(r => r.Pass);

    /// <summary>
    /// Whole ticks the window is behind at its end beyond the control's worst lag: tick lag swings by a tick or two
    /// with nothing running (frames and physics steps don't line up), so only lag past that jitter is lost.
    /// </summary>
    public static int Lost(WindowStats window, WindowStats control)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(control);
        var beyond = window.LagEnd - Math.Max(0, control.LagMax);
        return beyond >= 1 ? (int)Math.Floor(beyond) : 0;
    }

    private static int Long(WindowStats window, double overMs) => window.FrameTimes.Count(f => f > overMs + 1e-9);
}

/// <summary>One window judged against its control: its worst frame and its count of slow frames, with the control's.</summary>
internal sealed record FrameVerdict(double Worst, int Slow, double ControlWorst, int ControlSlow, bool Pass);

/// <summary>
/// Whether something CS2 did produced a frame the game doesn't produce on its own. A window passes when its worst
/// frame is at most the control's worst, and it has at most as many slow frames as the control; a slow frame is one
/// over twice the control's median. Being exactly at either bound passes.
/// </summary>
internal static class FrameRule
{
    public static FrameVerdict Judge(WindowStats control, WindowStats window)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(window);
        var slowMs = 2 * control.FrameP50;
        var controlSlow = Slow(control, slowMs);
        var slow = Slow(window, slowMs);
        return new FrameVerdict(
            window.FrameMax,
            slow,
            control.FrameMax,
            controlSlow,
            window.FrameMax <= control.FrameMax + 1e-9 && slow <= controlSlow
        );
    }

    /// <summary>
    /// The game's own collections land in windows at random, so one failed take doesn't fail a budget: most of the
    /// takes must fail (two of three). No takes is not a pass.
    /// </summary>
    public static bool Pass(IReadOnlyCollection<FrameVerdict> takes)
    {
        ArgumentNullException.ThrowIfNull(takes);
        return takes.Count > 0 && 2 * takes.Count(t => !t.Pass) <= takes.Count;
    }

    /// <summary>
    /// The controls themselves disagree (their worst frames differ by more than twice), so the verdict rests on
    /// which control a take happened to get.
    /// </summary>
    public static bool Noisy(IReadOnlyCollection<WindowStats> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        return controls.Count > 1 && controls.Max(c => c.FrameMax) > 2 * controls.Min(c => c.FrameMax) + 1e-9;
    }

    private static int Slow(WindowStats window, double overMs) => window.FrameTimes.Count(f => f > overMs + 1e-9);
}
