using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>What the results area shows instead of, or above, its results.</summary>
internal abstract record BannerState
{
    /// <summary>A run was cancelled with <paramref name="Count"/> targets unfinished.</summary>
    public sealed record Cancelled(int Count, IReadOnlyList<string> Keys) : BannerState;

    /// <summary>The chosen setup can't be simulated.</summary>
    public sealed record SetupError(Note Reason) : BannerState;

    /// <summary>
    /// Something failed: the simulator host, a run, some targets, or saving or opening a run.
    /// <paramref name="Keys"/> are the failed run's targets, which a host failure offers to simulate in the game's
    /// process.
    /// </summary>
    public sealed record Failure(FailureKind Kind, Note Reason, string? RunId, IReadOnlyList<string> Keys) : BannerState
    {
        public bool HostKind => Kind is FailureKind.HostCrash or FailureKind.HostHang or FailureKind.HostStart;

        public static Failure Of(FailureRecord record, Run? current) =>
            new(
                record.Kind,
                record.Reason,
                record.RunId,
                current != null && current.Id == record.RunId ? current.Targets : []
            );
    }
}
