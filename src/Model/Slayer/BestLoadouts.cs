using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>Where Best loadouts is: fighting its choices, done with its picks, cancelled, or failed.</summary>
public enum BestLoadoutsState
{
    Running,
    Done,
    Cancelled,
    Failed,
}

/// <summary>
/// Best loadouts on one master for one side: the master fought once with the side's setup for every monster, then once
/// with every monster on each unlocked saved loadout, all from one seed, then <see cref="LoadoutPicks"/> over them.
/// Its fights are no saved run; the answer lives for the session, and putting it on a side is the caller's.
/// </summary>
public sealed class BestLoadouts(string master, Setup side, Goal goal, ulong seed, IReadOnlyList<string?> choices)
{
    public string Master { get; } = master;
    public Setup Side { get; } = side;
    public Goal Goal { get; } = goal;
    public ulong Seed { get; } = seed;

    /// <summary>The setup (null), then each unlocked loadout slot at the start.</summary>
    public IReadOnlyList<string?> Choices { get; } = choices;

    public List<(string? Choice, TargetResult Master)> Results { get; } = [];
    public BestLoadoutsState State { get; set; } = BestLoadoutsState.Running;
    public LoadoutPicksAnswer? Picks { get; set; }
    public Note? Error { get; set; }
    public bool IsActive => State == BestLoadoutsState.Running;
}

/// <summary>
/// Best loadouts on every master in turn, then each master's best setup (<see cref="BestMasters"/>): the masters at the
/// start, those done, and the ranking once all are.
/// </summary>
public sealed class BestMastersRun(IReadOnlyList<string> masters, Goal goal)
{
    public IReadOnlyList<string> Masters { get; } = masters;
    public Goal Goal { get; set; } = goal;
    public List<BestMaster> Done { get; } = [];
    public BestLoadoutsState State { get; set; } = BestLoadoutsState.Running;
    public Note? Error { get; set; }
    public bool IsActive => State == BestLoadoutsState.Running;

    /// <summary>The master under way; null once all are done.</summary>
    public string? Current => Done.Count < Masters.Count ? Masters[Done.Count] : null;

    public IReadOnlyList<BestMaster> Ranked => BestMasters.Ranked(Done, Goal);
}
