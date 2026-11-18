using MelvorMods.CombatSimulator2.Engine;

namespace MelvorMods.CombatSimulator2.Host;

internal static class HeartbeatReading
{
    /// <summary>A heartbeat as the engine and the panel read it, without the wire's types.</summary>
    public static HostReading Reading(this HeartbeatMessage beat)
    {
        ArgumentNullException.ThrowIfNull(beat);
        return new HostReading(
            beat.CpuPercent,
            beat.PrivateBytes,
            beat.PeakPrivateBytes,
            beat.AvailableBytes,
            beat.Copies,
            beat.Stock,
            beat.StockTarget,
            beat.Warm.ToString().ToLowerInvariant(),
            beat.HeapBytes,
            beat.HeapCommittedBytes,
            beat.RunPeakPrivateBytes
        );
    }
}
