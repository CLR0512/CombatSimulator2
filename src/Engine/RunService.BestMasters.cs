using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class RunService
{
    private readonly Dictionary<string, BestLoadouts> _bestLoadoutsByMaster = new(StringComparer.Ordinal);

    /// <summary>The session's run over every master, running or ended; null before the first.</summary>
    public BestMastersRun? BestMasters { get; private set; }

    private readonly Dictionary<string, BestMaster> _bestByMaster = new(StringComparer.Ordinal);
    private Goal? _bestGoal;

    /// <summary>Each master's best from the session's latest runs over masters, all by one goal (another starts afresh).</summary>
    public IReadOnlyCollection<BestMaster> BestByMaster => _bestByMaster.Values;

    /// <summary>The goal <see cref="BestByMaster"/> was found by; null before any run over masters ends one.</summary>
    public Goal? BestGoal => _bestGoal;

    /// <summary>The session's last Best loadouts on <paramref name="master"/>, alone or in a run over every master.</summary>
    public BestLoadouts? BestLoadoutsFor(string master) => _bestLoadoutsByMaster.GetValueOrDefault(master);

    /// <summary>
    /// The masters Best loadouts for every master can take on <paramref name="side"/>: those it has unlocked (or every
    /// one the build allows, ignoring requirements), as master keys with the currency their blocks cost.
    /// </summary>
    public static IReadOnlyList<(string Master, string Coin)> BestMastersOffered(Setup side)
    {
        ArgumentNullException.ThrowIfNull(side);
        if (!Dependencies.IsAvailable(Dependencies.SlayerMasters) || LiveGame.Find() is not { } game)
            return [];
        return
        [
            .. SlayerTasks
                .Targets(game)
                .Select(t => SlayerTasks.Read(game, t.Master!, side.Changes))
                .Where(m => m is not null && (m.Unlocked || side.IgnoreRequirements))
                .Select(m => (Ids.MasterKey(m!.Master), m.Facts.Currency)),
        ];
    }

    /// <summary>
    /// Best loadouts on the masters offered on the side (<see cref="BestMastersOffered"/>), or those of them in
    /// <paramref name="only"/>, one after another from one seed, then each master's best setup with its blocks;
    /// side B is left as it was (each master's best goes there only when used). Refused without an unlocked saved loadout,
    /// and while anything else runs. <paramref name="sideLine"/> is the side as one setup line, to tell later whether it
    /// has changed since.
    /// </summary>
    public BestMastersRun StartBestMasters(
        Setup side,
        Goal goal,
        RunRequest options,
        IReadOnlyCollection<string>? only = null,
        string? sideLine = null
    )
    {
        MainThread.Assert("RunService.StartBestMasters");
        if (IsStarting || IsActive)
            throw new CommandException("a run or search is still going: wait for it or cancel it first");
        if (!Dependencies.IsAvailable(Dependencies.SlayerMasters))
            throw new CommandException("Slayer masters are unsupported on this game build (see cs2 deps)");
        LiveGame.Require();
        var offered = BestMastersOffered(side).Select(m => m.Master).ToList();
        if (offered.Count == 0)
            throw new CommandException("No Slayer master is unlocked");
        if (only?.FirstOrDefault(m => !offered.Contains(m)) is { } missing)
            throw new CommandException($"{Ids.BriefKey(missing)} isn't an unlocked Slayer master");
        var masters = only == null ? offered : offered.Where(only.Contains).ToList();
        if (masters.Count == 0)
            throw new CommandException("No Slayer master chosen");
        var run = BestMasters = new BestMastersRun(masters, goal) { SideLine = sideLine };
        // One seed for every master, so each is fought as a run of it with that seed would fight it.
        var seeded = options with
        {
            Seed = options.Seed ?? NewSeed(),
        };
        Log.Info($"best masters: {masters.Count} masters, seed {seeded.Seed}");
        NextMaster(run, side, seeded);
        return run;
    }

    // The next master's Best loadouts, or the ranking when none is left.
    private void NextMaster(BestMastersRun run, Setup side, RunRequest options)
    {
        if (!run.IsActive)
            return;
        if (run.Current is not { } master)
        {
            run.State = BestLoadoutsState.Done;
            Log.Info($"best masters done: {string.Join(", ", run.Ranked.Select(m => Ids.BriefKey(m.Master)))}");
            _changed = true;
            return;
        }
        BeginBestLoadouts(
            master,
            side,
            run.Goal,
            options,
            job =>
            {
                run.Goal = job.Goal; // main-skill XP stands as Slayer XP, as each master's own run took it
                var best =
                    BestMastersOf(job) ?? new BestMaster(job.Master, null, null, [], null, 0, null, null, null, null);
                run.Done.Add(best);
                if (_bestGoal != run.Goal)
                    _bestByMaster.Clear();
                _bestGoal = run.Goal;
                _bestByMaster[best.Master] = best;
                NextMaster(run, side, options);
            },
            run.SideLine
        );
    }

    private static BestMaster? BestMastersOf(BestLoadouts job) =>
        Model.Slayer.BestMasters.For(
            job.Master,
            job.Results,
            job.Picks,
            job.Goal,
            TimeUnit.Hour,
            LiveGame.Prices(LiveGame.Require())
        );
}
