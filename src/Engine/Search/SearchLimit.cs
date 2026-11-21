namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// The player's limit on a search a slot at a time, shared out among its climbs: each climb, as it begins, gets an
/// equal share of what is left between itself and those still to come, so a climb that ends early leaves its time to
/// the others. A climb from the runner-up runs only when its climb's share has as long left as that climb took. With
/// no limit every climb runs to its end.
/// </summary>
internal sealed class SearchLimit(double? max)
{
    private double _climbEnds = double.PositiveInfinity;
    private double _climbBegan;

    /// <summary>Whether a climb was stopped at the limit.</summary>
    public bool Stopped { get; private set; }

    /// <summary>A climb begins <paramref name="elapsed"/> seconds into the search, <paramref name="after"/> more to come.</summary>
    public void ClimbBegins(double elapsed, int after)
    {
        _climbBegan = elapsed;
        _climbEnds = max is { } limit ? elapsed + Math.Max(0, limit - elapsed) / (1 + after) : double.PositiveInfinity;
    }

    /// <summary>
    /// Whether the climb under way has used its share, and stops after the step it is on: a step is never cut, as its
    /// sets would then hold results of fewer trials than the rest.
    /// </summary>
    public bool Over(double elapsed)
    {
        if (elapsed <= _climbEnds)
            return false;
        Stopped = true;
        return true;
    }

    /// <summary>Whether the climb's share has room left for a climb from its runner-up.</summary>
    public bool RoomForRestart(double elapsed) => max == null || _climbEnds - elapsed >= elapsed - _climbBegan;

    /// <summary>Whether any time is left for another climb.</summary>
    public bool TimeLeft(double elapsed) => max is not { } limit || elapsed < limit;

    /// <summary>The seconds the whole search has left by its limit; null without one.</summary>
    public double? Left(double elapsed) => max is { } limit ? Math.Max(0, limit - elapsed) : null;
}
