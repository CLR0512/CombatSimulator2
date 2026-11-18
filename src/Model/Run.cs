using System.Globalization;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

// Godot-free results model. Serialised by RunFile; ids are the game's namespaced ids.

public enum RunState
{
    Starting,
    Running,
    Finished,
    Cancelled,

    /// <summary>
    /// Ended by a failure outside the targets, such as the simulator host closing; see <see cref="Run.Error"/>.
    /// </summary>
    Error,
}

/// <summary>One simulation of a set of targets for one setup.</summary>
public sealed class Run
{
    public string Id { get; set; } = "";
    public Setup Setup { get; set; } = new();

    /// <summary>SHA-256 of the snapshot: the setup's content, so runs can tell a changed setup apart.</summary>
    public string SnapshotSha256 { get; set; } = "";
    public string GameBuild { get; set; } = "";
    public ulong Seed { get; set; }

    /// <summary>
    /// The skill the setup's attack style trains most (full id, Hitpoints aside); null in runs saved before it was
    /// recorded. Resolves the <c>xp:main</c> metric.
    /// </summary>
    public string? MainSkill { get; set; }
    public int Trials { get; set; } = 1000;
    public int Copies { get; set; }

    /// <summary>
    /// Where the copies ran: <c>host</c> (the simulator process) or <c>game</c> (the game's process).
    /// </summary>
    public string? SimulatedIn { get; set; }

    /// <summary>Why the run ended in <see cref="RunState.Error"/>, in plain words.</summary>
    public Note? Error { get; set; }
    public TrialLimits Limits { get; set; } = new();

    /// <summary>Target keys in the order requested.</summary>
    public List<string> Targets { get; set; } = [];
    public RunState State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public Dictionary<string, TargetResult> Results { get; set; } = new(StringComparer.Ordinal);

    public bool IsFinal => State is RunState.Finished or RunState.Cancelled or RunState.Error;

    /// <summary>Targets with a final state.</summary>
    public int Done => Targets.Count(k => Results.TryGetValue(k, out var r) && r.IsFinal);

    /// <summary>Where the run's time went; null for runs saved before timings were kept.</summary>
    public RunTimings? Timings { get; set; }

    /// <summary>Whether reusing results from earlier runs was off, so every target was simulated.</summary>
    public bool Fresh { get; set; }

    /// <summary>
    /// Targets stop early once their kills rate is within this relative precision (0.05 = ±5%); null when off.
    /// </summary>
    public double? StopPrecision { get; set; }

    /// <summary>Whether the player set the seed, so only results simulated with that seed are reused.</summary>
    public bool SeedSet { get; set; }

    /// <summary>
    /// A hash of everything about the setup that can reach a fight, from the live game when the run started (parts
    /// that change on their own, such as timers and statistics, left out); null for runs saved before it was kept.
    /// Results are reused only between runs with the same one.
    /// </summary>
    public string? SetupHash { get; set; }

    /// <summary>
    /// The CS2 build (assembly MVID) that simulated the run: results from another build are never reused.
    /// </summary>
    public string? Cs2Mvid { get; set; }

    /// <summary>
    /// Every change tried for this run's setup, as checked when it started (applied, refused with why, or already so);
    /// null for runs without changes. Shown beside the results, never part of the results hash.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChangeOutcome>? ChangeOutcomes { get; set; }

    /// <summary>The player's name for the run (see <see cref="RunHistory.CleanName"/>); null for none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Whether the run is kept however many runs come after it, until unpinned.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Pinned { get; set; }

    /// <summary>
    /// False when the run was made on a game build CS2 wasn't checked with, or one it couldn't read; left out when it
    /// was checked. A run saved before this was recorded reads as null: no mark.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Checked { get; set; }
}

/// <summary>
/// Where a run's time went, in seconds (counts aside). Phases overlap across copies, so the figures summed over
/// copies and the wall times need not add up to <see cref="Total"/>.
/// </summary>
public sealed class RunTimings
{
    /// <summary>Taking and hashing the snapshot, on the main thread.</summary>
    public double Snapshot { get; set; }

    /// <summary>Finding results to reuse, on the main thread.</summary>
    public double ReuseLookup { get; set; }

    /// <summary>Waiting for the simulator host to start or to take the run.</summary>
    public double HostWait { get; set; }

    /// <summary>The host receiving and checking the run and its snapshot.</summary>
    public double Transfer { get; set; }

    /// <summary>Checking every target's eligibility on the first prepared copy.</summary>
    public double Check { get; set; }

    /// <summary>Copies built during the run.</summary>
    public int Builds { get; set; }

    /// <summary>Building copies, summed over copies.</summary>
    public double BuildsSummed { get; set; }

    /// <summary>Copies taken already built from the warm stock.</summary>
    public int FromStock { get; set; }

    /// <summary>Applying the snapshot to copies, summed over copies.</summary>
    public double PrepareSummed { get; set; }

    /// <summary>Copies reset after a target to fight another.</summary>
    public int Resets { get; set; }

    /// <summary>Resets that left the copy differing from its prepared state, so a fresh copy was used.</summary>
    public int ResetMisses { get; set; }

    /// <summary>Resetting copies, summed over copies.</summary>
    public double ResetSummed { get; set; }

    /// <summary>Fighting, summed over copies.</summary>
    public double FightSummed { get; set; }

    /// <summary>From the last result to the run saved.</summary>
    public double Report { get; set; }

    /// <summary>From the start to the first target with a final result.</summary>
    public double FirstResult { get; set; }

    /// <summary>From the start to the run's end.</summary>
    public double Total { get; set; }

    /// <summary>Targets answered from earlier runs.</summary>
    public int Reused { get; set; }

    /// <summary>Targets that fought in this run: not refused, not reused.</summary>
    public int Fought { get; set; }

    /// <summary>Fight slots the run used.</summary>
    public int Slots { get; set; }

    /// <summary>
    /// A slot neither building, preparing, checking, fighting nor resetting, from the run's start in the pool to
    /// the pool's end, summed over slots.
    /// </summary>
    public double IdleSummed { get; set; }

    /// <summary>From the second-to-last slot running out of targets to the pool's end; 0 with one slot.</summary>
    public double Tail { get; set; }

    /// <summary>Fills in what the pool measured.</summary>
    internal void Take(PoolStats stats)
    {
        HostWait = stats.HostWaitSeconds;
        Transfer = stats.TransferSeconds;
        Check = stats.CheckSeconds;
        Builds = stats.Built;
        BuildsSummed = stats.BuildSeconds;
        FromStock = stats.FromStock;
        PrepareSummed = stats.PrepareSeconds;
        Resets = stats.Resets;
        ResetMisses = stats.ResetMisses;
        ResetSummed = stats.ResetSeconds;
        FightSummed = stats.FightSeconds;
        Fought = stats.Fought;
        Slots = stats.Slots;
        IdleSummed = stats.IdleSeconds;
        Tail = stats.TailSeconds;
    }
}

/// <summary>The run a reused result was simulated in.</summary>
public sealed class ReuseOrigin
{
    public string RunId { get; set; } = "";
    public ulong Seed { get; set; }

    /// <summary>When that run started, UTC.</summary>
    public DateTime Date { get; set; }
}

/// <summary>Per-trial game-time limits, in seconds.</summary>
public sealed class TrialLimits
{
    public double EnemySeconds { get; set; } = 300;
    public double DungeonSeconds { get; set; } = 1800;

    /// <summary>
    /// The limit for a monster fought as part of a master: Auto Slayer waits as long as a kill takes, and some monsters
    /// it kills every time take over five minutes a kill, so the master's limit is far past a monster's own.
    /// </summary>
    /// <remarks>Null means the default, and keeps run files without masters as they were.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MasterPartSeconds { get; set; }

    public const double DefaultMasterPartSeconds = 3600;

    public double For(TargetKind kind) => kind == TargetKind.Dungeon ? DungeonSeconds : EnemySeconds;

    /// <summary>The limit for a target, or for a monster fought as part of a master when <paramref name="part"/>.</summary>
    public double For(TargetKind kind, bool part) => part ? MasterPartSeconds ?? DefaultMasterPartSeconds : For(kind);
}

public static class RunIds
{
    /// <summary>
    /// The next run id: <c>run-yyyyMMdd-HHmmss-n</c> with the smallest n not in <paramref name="existing"/>; a search's
    /// takes <c>search-</c> for <paramref name="prefix"/>.
    /// </summary>
    public static string Next(DateTime now, IEnumerable<string> existing, string prefix = "run-")
    {
        var taken = existing.ToHashSet(StringComparer.Ordinal);
        var stem = prefix + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-";
        for (var n = 1; ; n++)
        {
            var id = stem + n.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(id))
                return id;
        }
    }
}
