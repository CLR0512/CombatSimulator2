namespace MelvorMods.CombatSimulator2.Panel.Dev;

/// <summary>
/// What CS2's own per-frame callbacks allocate. The game allocates every frame itself, so a whole-frame figure says
/// nothing about CS2: this reads the main thread's allocation counter in a callback registered ahead of CS2's and
/// again in one registered after them, and sums the difference.
/// </summary>
internal static class FrameAlloc
{
    private static long _before = -1;
    private static long _bytes;
    private static int _frames;

    public static void Before() => _before = GC.GetAllocatedBytesForCurrentThread();

    public static void After()
    {
        if (_before < 0)
            return;
        _bytes += GC.GetAllocatedBytesForCurrentThread() - _before;
        _frames++;
        _before = -1;
        if (_probeLeft <= 0)
            return;
        _probeLeft--;
        for (var i = 0; i < _probed.Length; i++)
        {
            var at = GC.GetAllocatedBytesForCurrentThread();
            _probed[i].Call();
            _probeBytes[i] += GC.GetAllocatedBytesForCurrentThread() - at;
        }
    }

    private static (string Name, Action Call)[] _probed = [];
    private static long[] _probeBytes = [];
    private static int _probeLeft;

    /// <summary>
    /// For the next <paramref name="frames"/> frames, calls each callback once more a frame and counts what that
    /// call allocates, so work a callback does only now and then is caught where a burst of calls misses it.
    /// </summary>
    public static void Probe((string Name, Action Call)[] callbacks, int frames)
    {
        _probed = callbacks;
        _probeBytes = new long[callbacks.Length];
        _probeLeft = frames;
    }

    /// <summary>Bytes each probed callback allocated so far, and the frames still to go.</summary>
    public static string ProbeResult() =>
        string.Join("\n", _probed.Select((c, i) => $"{c.Name} {_probeBytes[i]} bytes"))
        + $"\n{_probeLeft} frames to go";

    /// <summary>Bytes allocated and frames counted since the last call; starts again from zero.</summary>
    public static (long Bytes, int Frames) Take()
    {
        var taken = (_bytes, _frames);
        _bytes = 0;
        _frames = 0;
        return taken;
    }
}
