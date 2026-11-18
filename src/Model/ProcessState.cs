namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// How a pool run used its copies and where its time went, in seconds: the most alive at once, how many were built
/// (and how long that took, summed over copies), and the other phases, summed over copies where they overlap.
/// </summary>
internal sealed record PoolStats(
    int PeakCopies,
    int Built,
    int PreBuiltUsed,
    double BuildSeconds,
    double WallSeconds,
    double CheckSeconds = 0,
    double PrepareSeconds = 0,
    int FromStock = 0,
    int Resets = 0,
    int ResetMisses = 0,
    double ResetSeconds = 0,
    double FightSeconds = 0,
    double TransferSeconds = 0,
    double HostWaitSeconds = 0,
    int Fought = 0,
    int Slots = 0,
    double IdleSeconds = 0,
    double TailSeconds = 0
);

/// <summary>The simulator host's state, as the live game sees it.</summary>
internal enum HostState
{
    NotStarted,
    Starting,
    Ready,
    Busy,
    Failed,
}

/// <summary>A game call a guard refused during a run: how often, and where it first came from.</summary>
internal sealed record BlockedGuard(string Name, int Count, string? Stack);
