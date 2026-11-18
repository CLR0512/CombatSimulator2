namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// Output is applied only for the current run's generation; anything older (or 0, none yet) is dropped.
/// </summary>
internal sealed class GenerationFilter
{
    private int _current;

    public int Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }

    public bool Accept(int generation) => generation != 0 && generation == Current;
}

/// <summary>
/// The host's one-run-at-a-time rule. A cancel frees it at once, even while the cancelled run's builds finish in
/// the background; a start while a run is active and uncancelled is a protocol error.
/// </summary>
internal sealed class RunGate
{
    private readonly Lock _lock = new();

    public int? Active { get; private set; }

    public void Start(int generation)
    {
        lock (_lock)
        {
            if (Active is { } active)
                throw new ProtocolException($"start of run {generation} while run {active} is still active");
            Active = generation;
        }
    }

    public void Cancel(int generation) => End(generation);

    /// <summary>The run finished, failed or was cancelled; another generation's end changes nothing.</summary>
    public void End(int generation)
    {
        lock (_lock)
        {
            if (Active == generation)
                Active = null;
        }
    }
}
