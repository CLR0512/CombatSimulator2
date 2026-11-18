using System.Diagnostics;
using System.Globalization;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// Times an open or a rebuild of CS2's window until its view is built, with the worst frame on the way. An open
/// compares with frames while closed; a rebuild of the open window with frames while it was open and idle.
/// </summary>
internal static class ModalTiming
{
    private static readonly Stopwatch _openClock = new();
    private static readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private static readonly Queue<double> _recentFrames = new(601);
    private static readonly Queue<double> _openFrames = new(601);
    private static bool _measuring;
    private static string _baselineOf = "closed";
    private static double _lastFrame;
    private static double _baselineP99;
    private static double _openWorstFrame;

    /// <summary>Milliseconds from the last open until the view was fully built and drawn; -1 before any open.</summary>
    public static double OpenedMs { get; private set; } = -1;

    /// <summary>True from an open until the view is fully built: slow frames are logged then.</summary>
    public static bool Measuring => _measuring;

    /// <summary>The worst frame of the last open, in milliseconds.</summary>
    public static double WorstFrame => _openWorstFrame;

    /// <summary>The 99th-percentile frame the last open was compared with.</summary>
    public static double BaselineP99 => _baselineP99;

    /// <summary>Whether that baseline was taken while the window was <c>open</c> or <c>closed</c>.</summary>
    public static string BaselineOf => _baselineOf;

    /// <summary>Starts timing an open, or with <paramref name="whileOpen"/> a rebuild of the open window.</summary>
    public static void Start(bool whileOpen)
    {
        _openClock.Restart();
        _measuring = true;
        _openWorstFrame = 0;
        var frames = whileOpen && _openFrames.Count > 0 ? _openFrames : _recentFrames;
        _baselineOf = ReferenceEquals(frames, _openFrames) ? "open" : "closed";
        var sorted = frames.Order().ToList();
        _baselineP99 = sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99))];
    }

    /// <summary>
    /// Records a frame: into a baseline while not timing (kept apart for <paramref name="isOpen"/>), and as the
    /// worst frame while timing, the open's own frame included.
    /// </summary>
    public static void Frame(bool isOpen)
    {
        var now = _frameClock.Elapsed.TotalMilliseconds;
        var frame = now - _lastFrame;
        _lastFrame = now;
        if (_measuring)
        {
            _openWorstFrame = Math.Max(_openWorstFrame, frame);
        }
        else
        {
            var frames = isOpen ? _openFrames : _recentFrames;
            frames.Enqueue(frame);
            if (frames.Count > 600)
                frames.Dequeue();
        }
    }

    /// <summary>Stops timing once the view is built and logs how long the open took.</summary>
    public static void Finish()
    {
        _measuring = false;
        OpenedMs = _openClock.Elapsed.TotalMilliseconds;
        Log.Info(
            $"opened in {OpenedMs.ToString("0", CultureInfo.InvariantCulture)} ms, worst frame "
                + $"{_openWorstFrame.ToString("0.0", CultureInfo.InvariantCulture)} ms"
        );
    }

    /// <summary>Logs a frame that took long while opening, with what it did.</summary>
    public static void Slow(string what, long startedAt)
    {
        if (!_measuring)
            return;
        var ms = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (ms > 4)
            Log.Info($"open: {what} {ms.ToString("0.0", CultureInfo.InvariantCulture)} ms");
    }

    public static void Reset()
    {
        _measuring = false;
        OpenedMs = -1;
    }
}
