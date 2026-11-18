namespace MelvorMods.CombatSimulator2.Host.Dev;

/// <summary>
/// A forced collection in the host, read from the heartbeats either side of it. <c>After</c>'s private bytes are
/// the host's live floor in <c>State</c> (<c>empty</c>, <c>stock</c> or <c>fighting n</c>).
/// </summary>
internal sealed record CollectReading(DateTime At, string State, HeartbeatMessage Before, HeartbeatMessage After)
{
    public long Freed => Before.PrivateBytes - After.PrivateBytes;
}

/// <summary>Pairs the heartbeat before a forced collection with the first one taken after it.</summary>
internal sealed class CollectWatch
{
    // The host collects on its heartbeat thread, so no heartbeat is read during a collection; one arriving sooner
    // than this after the ask was read before the host saw it.
    private static readonly TimeSpan AlreadySent = TimeSpan.FromMilliseconds(200);

    private readonly Lock _lock = new();
    private (DateTime At, HeartbeatMessage Before)? _asked;

    public CollectReading? Last { get; private set; }

    /// <summary>
    /// Has <paramref name="host"/> run one full compacting collection; false if it isn't running or hasn't sent a
    /// heartbeat yet.
    /// </summary>
    public bool Ask(HostClient host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.LastBeat is not { } before)
            return false;
        lock (_lock)
            _asked = (DateTime.UtcNow, before);
        if (host.Lever("collect"))
            return true;
        lock (_lock)
            _asked = null;
        return false;
    }

    public void Beat(HeartbeatMessage beat)
    {
        lock (_lock)
        {
            if (_asked is not { } asked || DateTime.UtcNow - asked.At < AlreadySent)
                return;
            _asked = null;
            var state =
                asked.Before.Generation != 0 ? $"fighting {asked.Before.Copies}"
                : asked.Before.Stock > 0 ? "stock"
                : "empty";
            Last = new CollectReading(asked.At, state, asked.Before, beat);
        }
    }
}
