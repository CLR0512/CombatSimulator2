using System.Diagnostics;

namespace MelvorMods.CombatSimulator2.Panel.Dev;

/// <summary>
/// Times any change to the window (an open, a close, a tab switch, a rebuild) the same way: from the action until
/// the window has settled and one more frame has passed, so the settled window was drawn. The shortest reading is
/// two frames.
/// </summary>
internal static class InteractionTiming
{
    private static readonly Stopwatch _clock = new();
    private static bool _pending;
    private static bool _settled;

    public static bool Pending => _pending;

    /// <summary>Milliseconds since the action being timed; what an action still pending took so far.</summary>
    public static double Elapsed => _clock.Elapsed.TotalMilliseconds;

    /// <summary>Call right after the action.</summary>
    public static void Start()
    {
        _clock.Restart();
        _pending = true;
        _settled = false;
    }

    /// <summary>Once a frame, after the window's own frame work: the time taken, on the frame it is known.</summary>
    public static double? Frame()
    {
        if (!_pending)
            return null;
        if (!_settled)
        {
            _settled = Cs2Modal.Settled;
            return null;
        }
        _pending = false;
        return _clock.Elapsed.TotalMilliseconds;
    }

    public static void Stop() => _pending = false;
}
