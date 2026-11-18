namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// The highest private bytes read while a run is active: the run's own peak, where the process's peak is its
/// lifetime's and can't be reset. Safe to share between the thread that reads memory and the ones that end runs.
/// </summary>
internal sealed class RunPeak
{
    private readonly Lock _lock = new();
    private bool _active;
    private long _bytes;

    /// <summary>The active run's peak so far; 0 when no run is active.</summary>
    public long Bytes
    {
        get
        {
            lock (_lock)
                return _active ? _bytes : 0;
        }
    }

    /// <summary>A run starts: its peak begins at what the process holds now.</summary>
    public void Start(long privateBytes)
    {
        lock (_lock)
        {
            _active = true;
            _bytes = privateBytes;
        }
    }

    /// <summary>A reading during the run; ignored when no run is active.</summary>
    public void Read(long privateBytes)
    {
        lock (_lock)
        {
            if (_active && privateBytes > _bytes)
                _bytes = privateBytes;
        }
    }

    /// <summary>
    /// The run ends, with one last reading so a run shorter than the reading interval still has its own; returns its
    /// peak, or 0 when no run was active.
    /// </summary>
    public long End(long privateBytes)
    {
        lock (_lock)
        {
            if (!_active)
                return 0;
            _active = false;
            return Math.Max(_bytes, privateBytes);
        }
    }
}
