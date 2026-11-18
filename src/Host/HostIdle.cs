namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// When a ready host with no run is stopped. With the panel open it may idle a long while: a run is likely. With
/// the panel closed it goes soon after, counted from the later of the panel closing and the last run's end, so a
/// run still going when the panel closes finishes and then gets the whole limit. Driven by a clock so it can be
/// tested.
/// </summary>
internal sealed class HostIdle(Func<DateTime> clock)
{
    public TimeSpan OpenLimit { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan ClosedLimit { get; set; } = TimeSpan.FromMinutes(2);

    private readonly Lock _lock = new();
    private bool _open;
    private DateTime _closedAt = DateTime.MinValue;

    /// <summary>The panel opened or closed. Until it has ever opened it counts as closed.</summary>
    public void Panel(bool open)
    {
        lock (_lock)
        {
            if (_open && !open)
                _closedAt = clock();
            _open = open;
        }
    }

    /// <summary>
    /// The limit the host has passed, idle since <paramref name="idleSince"/> (its start or its last run's end), or
    /// null while it may stay. A host that isn't idle (null) always stays.
    /// </summary>
    public TimeSpan? Passed(DateTime? idleSince)
    {
        if (idleSince is not { } since)
            return null;
        lock (_lock)
        {
            var (limit, from) = _open ? (OpenLimit, since) : (ClosedLimit, since > _closedAt ? since : _closedAt);
            return clock() - from > limit ? limit : null;
        }
    }
}
