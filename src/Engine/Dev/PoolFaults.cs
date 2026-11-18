namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>
/// Fault levers for cs2dev host: an ordinary exception where a copy fights or is prepared, to check that only the
/// target in hand fails. A throw can wait for one target, so a rerun fails the same target whatever order it
/// fights in.
/// </summary>
internal static class PoolFaults
{
    private static int _throwNextFight;
    private static int _throwNextPrepare;
    private static string? _throwOn;

    /// <summary>The next target to fight in any pool of this process throws.</summary>
    public static void ThrowNextFight() => Volatile.Write(ref _throwNextFight, 1);

    /// <summary>The next fight of <paramref name="key"/> in any pool of this process throws.</summary>
    public static void ThrowOn(string key) => Volatile.Write(ref _throwOn, key);

    /// <summary>The next copy prepared for a run in this process throws, as a failed build would.</summary>
    public static void ThrowNextBuild() => Volatile.Write(ref _throwNextPrepare, 1);

    public static void BeforeFight(string key)
    {
        if (Interlocked.Exchange(ref _throwNextFight, 0) == 1)
            throw new InvalidOperationException("cs2dev throw");
        // Cleared against the stored string itself: the exchange compares references, and the key fought is
        // another string with the same text.
        if (
            Volatile.Read(ref _throwOn) is { } armed
            && armed == key
            && ReferenceEquals(Interlocked.CompareExchange(ref _throwOn, null, armed), armed)
        )
            throw new InvalidOperationException("cs2dev throw " + key);
    }

    public static void BeforePrepare()
    {
        if (Interlocked.Exchange(ref _throwNextPrepare, 0) == 1)
            throw new InvalidOperationException("cs2dev throw build");
    }
}
