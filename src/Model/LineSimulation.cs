namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What a simulation line holds beyond a setup line: the other side when comparing, each side's "ignore
/// requirements", the targets the page leaves out, and the Simulate settings. Never the character's save, a name, the
/// copy count or where runs simulate: those are the player's own.
/// </summary>
/// <param name="B">Side B's entries; null when not comparing.</param>
/// <param name="Ignored">Target keys, as ids.</param>
/// <param name="Seed">Null: a new seed each run.</param>
/// <param name="Limits">Null: the default limits.</param>
/// <param name="Metric">The "Rank by" key, in full.</param>
public sealed record LineSimulation(
    IReadOnlyList<LineEntry> A,
    IReadOnlyList<LineEntry>? B,
    bool IgnoreRequirementsA,
    bool IgnoreRequirementsB,
    IReadOnlyList<LineId> Ignored,
    int Trials,
    ulong? Seed,
    bool Fresh,
    bool StopEarly,
    double StopPrecision,
    TrialLimits? Limits,
    LineId Metric,
    TimeUnit TimeUnit
)
{
    public const int MostTrials = 100_000;

    /// <summary>The most targets a line leaves out: every target a build has, and room to grow.</summary>
    public const int MostIgnored = 400;

    /// <summary>The longest per-trial limit, in seconds, a line may set.</summary>
    public const int LongestLimit = 86_400;
}
