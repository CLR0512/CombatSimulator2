using MelvorMods.CombatSimulator2.Panel.Dev;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// What a bench does to the window during one frame window: actions repeated at a fixed pace, some of them timed
/// until the window is usable again, and work done every frame (a scroll, keeping a run going).
/// </summary>
internal sealed class BenchDrive
{
    private readonly List<(double Period, double At, string? Timed, Action Do)> _beats = [];
    private readonly List<int> _fired = [];
    private readonly List<Action<double>> _frames = [];
    private Action? _end;
    private string? _timing;

    /// <summary>Time to usable per repeat, by the name each timed action was given.</summary>
    public Dictionary<string, List<double>> Usable { get; } = new(StringComparer.Ordinal);

    /// <summary>Why the window measured nothing (a list that isn't shown); null when it did.</summary>
    public string? Problem { get; set; }

    /// <summary>
    /// Every <paramref name="period"/> seconds, <paramref name="at"/> seconds in: <paramref name="act"/>, timed
    /// under <paramref name="timed"/> when it has a name.
    /// </summary>
    public BenchDrive Beat(double period, double at, string? timed, Action act)
    {
        _beats.Add((period, at, timed, act));
        _fired.Add(0);
        if (timed != null)
            Usable[timed] = [];
        return this;
    }

    public BenchDrive EveryFrame(Action<double> frame)
    {
        _frames.Add(frame);
        return this;
    }

    public BenchDrive AtEnd(Action end)
    {
        _end = end;
        return this;
    }

    /// <summary>Once a frame, <paramref name="t"/> seconds into the window.</summary>
    public void Frame(double t)
    {
        if (InteractionTiming.Frame() is { } ms && _timing != null)
        {
            Usable[_timing].Add(ms);
            _timing = null;
        }
        for (var i = 0; i < _beats.Count; i++)
        {
            var (period, at, timed, act) = _beats[i];
            if (t < at || (int)((t - at) / period) < _fired[i])
                continue;
            _fired[i] = (int)((t - at) / period) + 1;
            // Still not usable when the next action comes: it counts for what it took so far, which is too long.
            if (_timing != null)
            {
                Usable[_timing].Add(InteractionTiming.Elapsed);
                _timing = null;
            }
            act();
            if (timed != null)
            {
                _timing = timed;
                InteractionTiming.Start();
            }
        }
        foreach (var frame in _frames)
            frame(t);
    }

    /// <summary>The window is over; an action still being timed is left out.</summary>
    public void Finish()
    {
        _timing = null;
        InteractionTiming.Stop();
        _end?.Invoke();
    }
}
