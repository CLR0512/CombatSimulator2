using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class RunService
{
    private const string SlayerXp = "xp:MelvorBase:Slayer";
    private CancellationTokenSource? _loadoutsCancel;

    /// <summary>The session's Best loadouts, running or ended; null before the first.</summary>
    public BestLoadouts? BestLoadouts { get; private set; }

    /// <summary>
    /// Starts Best loadouts on <paramref name="masterKey"/> for <paramref name="side"/>; <paramref name="done"/> runs on
    /// the main thread when it ends with picks. Refused without an unlocked saved loadout, and while a run or search is
    /// going.
    /// </summary>
    public BestLoadouts StartBestLoadouts(
        string masterKey,
        Setup side,
        Goal goal,
        RunRequest options,
        Action<BestLoadouts> done
    )
    {
        MainThread.Assert("RunService.StartBestLoadouts");
        if (IsStarting || IsActive)
            throw new CommandException("a run or search is still going: wait for it or cancel it first");
        if (!Dependencies.IsAvailable(Dependencies.SlayerMasters))
            throw new CommandException("Slayer masters are unsupported on this game build (see cs2 deps)");
        return BeginBestLoadouts(masterKey, side, goal, options, done);
    }

    // Best loadouts on one master, its checks made by the caller.
    private BestLoadouts BeginBestLoadouts(
        string masterKey,
        Setup side,
        Goal goal,
        RunRequest options,
        Action<BestLoadouts> done
    )
    {
        var game = LiveGame.Require();
        var loadouts = LiveGame.Loadouts(game).Where(l => l.Unlocked).Select(l => l.Slot).ToList();
        if (loadouts.Count == 0)
            throw new CommandException("No saved loadouts to try");
        var masterId = Ids.MasterOf(masterKey);
        var spec =
            SlayerTasks.Read(game, masterId, side.Changes)
            ?? throw new CommandException($"no Slayer master '{Ids.Brief(masterId)}'");
        var slot = CheckLoadout(side, () => LiveGame.Loadouts(game));
        var snapshot = LiveGame.TakeSnapshot(game);
        side.Character = snapshot.Character;
        // Every monster on the side's setup is the baseline: a loadouts change the side has for this master is left out.
        var setup = side.Changes?.Without(new ChangeKey(ChangeKind.Loadouts, masterId));
        // Each loadout trains its own skill, so "main skill" XP would compare different skills: every choice earns
        // Slayer XP on a master's tasks, so that stands for it.
        if (goal.Metric == Metrics.MainXp)
            goal = goal with { Metric = SlayerXp };
        var job = new BestLoadouts(masterKey, side, goal, options.Seed ?? NewSeed(), [null, .. loadouts]);
        BestLoadouts = _bestLoadoutsByMaster[masterKey] = job;
        _loadoutsCancel?.Dispose();
        _loadoutsCancel = new CancellationTokenSource();
        var where = options.Where ?? DefaultWhere();
        Log.Info(
            $"best loadouts on {Ids.BriefKey(masterKey)}: {job.Choices.Count} choices, seed {job.Seed}, {options.Trials} trials, in {where}"
        );
        FightChoice(job, spec, snapshot, slot, setup, options, where, done, _loadoutsCancel.Token);
        _changed = true;
        return job;
    }

    /// <summary>Cancels Best loadouts; nothing it would have changed is touched.</summary>
    public void CancelBestLoadouts()
    {
        MainThread.Assert("RunService.CancelBestLoadouts");
        if (BestLoadouts is not { IsActive: true } job)
            throw new CommandException("Best loadouts isn't running");
        job.State = BestLoadoutsState.Cancelled;
        if (BestMasters is { IsActive: true } all)
            all.State = BestLoadoutsState.Cancelled;
        _loadoutsCancel?.Cancel();
        _generation++; // what its fights still send is dropped
        _changed = true;
    }

    // One choice's master fight, then the next, then the picks.
    private void FightChoice(
        BestLoadouts job,
        MasterSpec spec,
        Snapshot snapshot,
        string? slot,
        ChangeList? setup,
        RunRequest options,
        string where,
        Action<BestLoadouts> done,
        CancellationToken token
    )
    {
        var choice = job.Choices[job.Results.Count];
        var generation = ++_generation;
        var parts = spec.Pool.ToList();
        var pool = new PoolJob(
            snapshot,
            slot,
            parts,
            new DriveOptions(options.Trials, options.Limits ?? new TrialLimits(), job.Side.IgnoreRequirements),
            job.Seed,
            options.Copies,
            ReuseCopies,
            // The next choice's copies are built while this one fights, as a comparison's second side's are.
            NextRunCopies: job.Results.Count + 1 < job.Choices.Count ? options.Copies : 0,
            Changes: setup is { IsEmpty: false } ? setup : null,
            PartLoadouts: choice == null ? null : parts.ToDictionary(p => p.Key, _ => choice, StringComparer.Ordinal)
        );
        var report = new ChoiceReport(
            _queue,
            generation,
            results =>
            {
                if (!job.IsActive)
                    return;
                var master = new TargetResult
                {
                    Key = job.Master,
                    Kind = TargetKind.Master,
                    State = TargetState.Queued,
                    Blocked = [.. spec.Blocked],
                    Master = spec.Facts,
                    Parts = [.. parts.Select(p => results.GetValueOrDefault(p.Key) ?? Missing(p))],
                };
                MasterParts.Settle(master);
                job.Results.Add((choice, master));
                _changed = true;
                if (job.Results.Count < job.Choices.Count)
                {
                    FightChoice(job, spec, snapshot, slot, setup, options, where, done, token);
                    return;
                }
                job.Picks = LoadoutPicks.For(job.Results, job.Goal, TimeUnit.Hour, LiveGame.Prices(LiveGame.Require()));
                job.State = BestLoadoutsState.Done;
                Log.Info(
                    $"best loadouts on {Ids.BriefKey(job.Master)} done: {job.Picks?.Change?.ShortForm ?? "the setup for all"}"
                );
                done(job);
            },
            error =>
            {
                if (!job.IsActive)
                    return;
                job.State = BestLoadoutsState.Failed;
                job.Error = Note.Of("failed: {0}", error.Message);
                if (BestMasters is { IsActive: true } all)
                    (all.State, all.Error) = (BestLoadoutsState.Failed, job.Error);
                Log.Error("best loadouts failed", error);
                _changed = true;
            }
        );
        if (where == InHost)
            (Remote ?? throw new InvalidOperationException("no simulator host")).Start(pool, report, generation, token);
        else
            CopyPool.Start(pool, report, token);
    }

    private static TargetResult Missing(TargetSpec part) =>
        new()
        {
            Key = part.Key,
            Kind = part.Kind,
            State = TargetState.Cancelled,
            Reasons = [Note.Of("cancelled")],
        };

    // A choice's pool output, gathered on worker threads and handed to the main thread when it ends.
    private sealed class ChoiceReport(
        ResultQueue queue,
        int generation,
        Action<Dictionary<string, TargetResult>> finished,
        Action<Exception> failed
    ) : IPoolReport
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TargetResult> _results = new(
            StringComparer.Ordinal
        );

        public void State(string key, TargetState state) { }

        public void MainSkill(string? skill) { }

        public void Progress(string key, int trialsDone) { }

        public void Done(TargetResult result) => _results[result.Key] = result;

        public void CopyDropped(string error) => Log.Warn($"best loadouts: a copy was dropped: {error}");

        public void Finished(PoolStats stats) =>
            queue.Post(
                generation,
                () => finished(new Dictionary<string, TargetResult>(_results, StringComparer.Ordinal))
            );

        public void Failed(Exception error) => queue.Post(generation, () => failed(error));
    }
}
