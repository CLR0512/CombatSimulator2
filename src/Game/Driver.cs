using System.Diagnostics;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// How targets are fought. <c>StopPrecision</c>: stop a target early at that relative precision; null for all trials.
/// </summary>
internal sealed record DriveOptions(
    int Trials,
    TrialLimits Limits,
    bool IgnoreRequirements,
    double? StopPrecision = null
);

/// <summary>
/// Fights one target in a copy with the game's own combat and turns its events into trials.
/// Runs on the copy's thread. The copy must already hold the run's snapshot, reseeded and with time frozen.
/// </summary>
internal static partial class Driver
{
    private const int CancelCheckTicks = 1000;

    /// <summary>
    /// Fights <paramref name="target"/> as judged by <paramref name="eligibility"/>, which the caller took on a
    /// prepared copy before any fight: a copy that has fought (kill counts, dungeon completions) could answer
    /// differently.
    /// </summary>
    public static TargetResult Fight(
        Copy copy,
        TargetSpec target,
        Eligibility eligibility,
        DriveOptions options,
        CancellationToken token,
        Action<int>? onTrial = null,
        DriveChecks? checks = null
    )
    {
        using var scope = Guards.Enter();
        using var conditions = copy.CollectConditions();
        var watch = Stopwatch.StartNew();
        var result = new TargetResult
        {
            Key = target.Key,
            Kind = target.Kind,
            State = TargetState.Fighting,
        };
        using var collect = GuardLog.CollectFor(result.Unsupported);
        // The game doesn't enforce requirements itself: the target is judged as of the run's snapshot.
        if (!eligibility.Eligible)
        {
            if (!options.IgnoreRequirements)
            {
                result.State = TargetState.Refused;
                result.Reasons.AddRange(eligibility.Reasons);
                result.RealSeconds = watch.Elapsed.TotalSeconds;
                return result;
            }
            result.Assumptions.Add(Note.Of("requirements ignored: {0}", eligibility.Reasons.English(" | ")));
        }
        if ((target.Task ?? target.SingleMaster) is { } master)
            SlayerTasks.Force(copy.Game, target, master);
        else if (target.SingleTask != null)
            SlayerTasks.Clear(copy.Game);
        // A master plays its tasks through, each to its end; a monster or dungeon fought as the save has it keeps the
        // save's task for the whole run.
        else if (target.Kind is TargetKind.Enemy or TargetKind.Dungeon)
            SlayerTasks.KeepForRun(copy.Game);
        result.Task = target.SingleMaster;
        if (target.SingleTask == TargetSpec.NoMaster)
            result.TaskNote = Note.Of("no master gives it: off task");
        result.NotSimulated = NotSimulated(copy, target);
        using var tallying = new Tallying(copy);
        var adjustments = new Adjustments(copy, tallying);
        adjustments.Start();
        result.Assumptions.InsertRange(0, Adjustments.Assumptions);
#if DEBUG
        var allocated = GC.GetAllocatedBytesForCurrentThread();
#endif
        var fight = new Bout(copy, target, options, tallying, adjustments, result, checks);
        try
        {
            fight.Loop(onTrial, token);
        }
        finally
        {
            fight.Unsubscribe();
        }
#if DEBUG
        Common.Log.Info(
            $"alloc: fight {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576.0:0} MB over {result.TrialsDone} trials, {result.GameSeconds:0} game s"
        );
        var drift = adjustments.Drift();
        Common.Log.Info(
            $"{target.Key}: copy drift after {result.TrialsDone} trials: {(drift.Count == 0 ? "none" : string.Join("; ", drift))}"
        );
#endif
        result.RealSeconds = watch.Elapsed.TotalSeconds;
        return result;
    }

    private sealed partial class Bout
    {
        private readonly Copy _copy;
        private readonly CombatEntity _combat;
        private readonly DriveOptions _options;
        private readonly Tallying _tallying;
        private readonly Adjustments _adjustments;
        private readonly TargetResult _result;
        private readonly CombatArea? _area;
        private readonly Enemy? _enemy;
        private readonly Dungeon? _dungeon;
        private readonly double _tickSeconds = Constants.TickInterval / 1000;
        private readonly int _batchCount;
        private long _ticks;
        private long _trialStart;
        private long _fightStart;
        private int _stage;
        private long _fightDamage;
        private readonly FightCounting? _counting;
        private long? _spawnTick; // the tick the enemy in front of the player spawned at; null between fights
        private FightOutcome? _ended; // set by an event during a tick, closed after it

        public Bout(
            Copy copy,
            TargetSpec target,
            DriveOptions options,
            Tallying tallying,
            Adjustments adjustments,
            TargetResult result,
            DriveChecks? checks
        )
        {
            _checks = checks == null ? null : new CheckTallies(checks, options.Trials);
            _copy = copy;
            _combat = copy.Combat;
            _options = options;
            _tallying = tallying;
            _adjustments = adjustments;
            _result = result;
            _batchCount = Batches.Count(options.Trials);
            for (var i = 0; i < _batchCount; i++)
                result.Batches.Add(new Tally());
            var area = Targets.Area(copy.Game, target);
            if (area is Dungeon dungeon)
            {
                _dungeon = dungeon;
                if (tallying.RecordsRisk)
                    result.Stages = StageCount(dungeon);
            }
            else
            {
                _area = (CombatArea)area;
                _enemy = copy.Game.Data.GetObject<Enemy>(target.Enemy!);
            }
            if (tallying.RecordsRisk)
                result.MaxHp = PlayerMaxHp();
            if (Dependencies.IsAvailable(Dependencies.Drops))
                result.Drops = ReadDrops();
            _combat.Enemy.Killed += Killed;
            _combat.Action.PlayerDied += PlayerDied;
            _combat.Areas.DungeonCompleted += DungeonCompleted;
            // Without the health event, a fight's damage falls back to attacks only, as runs counted it before.
            if (!tallying.RecordsRisk)
                _combat.Player.Defended += Defended;
            _counting = tallying.Counting;
            if (_counting != null)
                SubscribeSpawned();
        }

        private void SubscribeSpawned() => _combat.Enemy.Spawned += Spawned;

        private void UnsubscribeSpawned() => _combat.Enemy.Spawned -= Spawned;

        public void Unsubscribe()
        {
            _combat.Enemy.Killed -= Killed;
            _combat.Action.PlayerDied -= PlayerDied;
            _combat.Areas.DungeonCompleted -= DungeonCompleted;
            if (!_tallying.RecordsRisk)
                _combat.Player.Defended -= Defended;
            if (_counting != null)
                UnsubscribeSpawned();
        }

        // Its own method so a build without the members only loses the rare drops.
        private List<DropChance> ReadDrops() => Drops.Of(_combat, _enemy, _dungeon);

        // Its own method so a build without the member only loses the stage count.
        private static int StageCount(Dungeon dungeon) => dungeon.EnemyCount;

        public void Loop(Action<int>? onTrial, CancellationToken token)
        {
            var limit = _options.Limits.For(_result.Kind, part: Ids.IsPart(_result.Key));
            var limitTicks = (long)Math.Ceiling(limit / _tickSeconds);
            Select();
            while (_result.TrialsDone < _options.Trials)
            {
                if (_ticks % CancelCheckTicks == 0 && token.IsCancellationRequested)
                {
                    _result.State = TargetState.Cancelled;
                    _result.Reasons.Add(
                        Note.Of("cancelled after {0} of {1} trials", _result.TrialsDone, _options.Trials)
                    );
                    return;
                }
                if (_combat.Action.IsPaused)
                    _combat.Action.Unpause(); // enemies that pause on spawn wait for the player
                _combat.Tick();
                _ticks++;
                _result.GameSeconds += _tickSeconds;
                var stop = false;
                if (_ended is { } outcome)
                {
                    _ended = null;
                    EndTrial(outcome);
                    onTrial?.Invoke(_result.TrialsDone);
                    stop = StopsEarly() || GivesUp() || AtCheck();
                    if (outcome == FightOutcome.Death && !stop)
                        Select(); // death stops combat and resets dungeon progress
                }
                _adjustments.AfterTick();
                if (stop)
                    return; // after the tick's loot is counted and cleared, as at any trial's end
                if (_ticks - _trialStart >= limitTicks)
                {
                    if (_counting != null)
                    {
                        if (RanOver(limit, onTrial))
                            return;
                        continue;
                    }
                    _result.State = TargetState.Unfinished;
                    _result.Reasons.Add(
                        Note.Of(
                            "a trial ran over the {0:0} s limit after {1} finished trials",
                            limit,
                            _result.TrialsDone
                        )
                    );
                    return;
                }
            }
            _result.State = TargetState.Finished;
        }

        // A trial over the limit with none finished ends the target with the reason the counts show. Once a trial has
        // finished, the target can be fought, so the trial is closed as one that ran over (neither kill nor death,
        // its time, XP, loot and counts kept) and the fight starts again. Returns whether the target is done.
        private bool RanOver(double limit, Action<int>? onTrial)
        {
            FightEnded(_ticks);
            var stall = TakeStall(limit);
            if (_result.TrialsDone == 0)
            {
                _result.State = TargetState.Unfinished;
                _result.Stall = stall;
                _result.Reasons.Add(StallReasons.Note(stall, EnemyName()));
                return true;
            }
            if (_result.Stall is { } earlier)
                earlier.Add(stall);
            else
                _result.Stall = stall;
            if (_dungeon != null)
                Record(_combat.Enemy.Enemy?.Id ?? "", (_ticks - _fightStart) * _tickSeconds, FightOutcome.RanOver);
            EndTrial(FightOutcome.RanOver);
            onTrial?.Invoke(_result.TrialsDone);
            if (_result.TrialsDone >= _options.Trials)
            {
                _result.State = TargetState.Finished;
                return true;
            }
            if (StopsEarly() || GivesUp() || AtCheck())
                return true;
            Select();
            return false;
        }

        // After a trial that completes a batch: a target whose trials run over too often ends unfinished with how
        // often and why, rather than fight up to the limit for each of many more.
        private bool GivesUp()
        {
            if (_result.Stall is not { } stall || _result.TrialsDone >= _options.Trials)
                return false;
            var batch = Batches.IndexOf(_result.TrialsDone, _options.Trials);
            if (batch == Batches.IndexOf(_result.TrialsDone - 1, _options.Trials))
                return false;
            var complete = _result.Batches.GetRange(0, batch);
            if (!StallReasons.TooOftenOver(complete))
                return false;
            _result.State = TargetState.Unfinished;
            _result.Reasons.Add(
                StallReasons.GaveUp(
                    stall,
                    _result.RanOver,
                    _result.TrialsDone,
                    stall.Enemy is { } id ? EnemyNameOf(id) : null
                )
            );
            return true;
        }

        private string? EnemyNameOf(string id) => _copy.Game.Data.GetObject<Enemy>(id).DisplayName.Text;

        // Read before the trial is closed. A dungeon's counts are the stalled fight's alone, so the reason speaks of
        // the enemy it names; earlier stages of the trial were won.
        private Stall TakeStall(double limit)
        {
            var enemy = _combat.Enemy;
            return new Stall
            {
                Trials = 1,
                Counts = (_dungeon != null ? _counting!.Fight : _counting!.Trial).Copy(),
                HpLeft = enemy.Health.Current,
                HpMax = enemy.Health.Max,
                WardLeft = enemy.Ward.Current,
                Immune = _combat.Player.TargetStats?.ImmunityFrom.Total.Value > 0,
                LimitSeconds = limit,
                Stage = _dungeon != null ? _stage + 1 : null,
                Enemy = _dungeon != null ? enemy.Enemy?.Id : null,
            };
        }

        private string? EnemyName() => _dungeon != null ? _combat.Enemy.Enemy?.DisplayName.Text : null;

        // After a trial that completes a batch: with a precision asked, stop once the kills rate is known well enough,
        // keeping only the complete batches.
        private bool StopsEarly()
        {
            if (_options.StopPrecision is not { } precision || _result.TrialsDone >= _options.Trials)
                return false;
            var batch = Batches.IndexOf(_result.TrialsDone, _options.Trials);
            if (batch == Batches.IndexOf(_result.TrialsDone - 1, _options.Trials))
                return false; // the batch isn't complete yet
            var complete = _result.Batches.GetRange(0, batch);
            if (!EarlyStop.ShouldStop(complete, precision))
                return false;
            _result.Batches.RemoveRange(batch, _result.Batches.Count - batch);
            _result.State = TargetState.Finished;
            _result.StoppedEarly = true;
            _result.Assumptions.Add(
                Note.Of(
                    "stopped early at ±{0}% after {1} of {2} trials",
                    precision * 100,
                    _result.TrialsDone,
                    _options.Trials
                )
            );
            return true;
        }

        private void Select()
        {
            _fightStart = _ticks;
            _stage = 0;
            if (_dungeon != null)
                _combat.Areas.SelectDungeon(_dungeon);
            else
                _combat.Areas.SelectEnemy(_area!, _enemy!);
        }

        // A trial is closed after the tick that ended it, so loot and XP from that tick belong to it.
        private void EndTrial(FightOutcome outcome)
        {
            var tally = _tallying.Trial;
            tally.Trials = 1;
            tally.Deaths = outcome == FightOutcome.Death ? 1 : 0;
            tally.Kills = outcome == FightOutcome.Kill ? 1 : 0;
            tally.GameSeconds = (_ticks - _trialStart) * _tickSeconds;
            if (_tallying.RecordsRisk)
                TakeRisk(tally, outcome);
            if (_counting != null)
            {
                tally.Fight = _counting.Trial;
                tally.RanOver = outcome == FightOutcome.RanOver ? 1 : 0;
            }
            _result.Batches[Batches.IndexOf(_result.TrialsDone, _options.Trials)].Add(tally);
            _checks?.Count(tally, _result.TrialsDone);
            tally.Clear();
            _counting?.Trial.Clear();
            _counting?.Fight.Clear();
            _result.TrialsDone++;
            _trialStart = _ticks;
            _adjustments.HoldLevels();
        }

        // A death keeps the trial's highest hit but not its lowest HP (0): the lowest HP is over survived trials. A
        // survived trial with no hit counts the HP it ended with.
        private void TakeRisk(Tally tally, FightOutcome outcome)
        {
            var hits = _tallying.TrialHits;
            tally.MaxHit = hits.MaxHit ?? 0;
            tally.MaxReflect = hits.MaxReflect ?? 0;
            if (outcome == FightOutcome.Kill)
            {
                var lowest = hits.MinHp ?? PlayerHp();
                tally.MinHp = lowest;
                tally.CloseCalls = hits.MaxHit is { } highest && highest >= lowest ? 1 : 0;
            }
            else
                tally.CloseCalls = 0;
            hits.Reset();
            _tallying.FightHits.Reset();
        }

        private long PlayerHp() => _combat.Player.GlobalHealth.Current;

        private long PlayerMaxHp() => _combat.Player.GlobalHealth.Max;

        // Fight time runs from the enemy's spawn, so the wait for the next one stays out of damage per second. Events
        // come during a tick, before its count goes up, so that tick is the fight's own.
        private void Spawned(object? sender, EnemyEventArgs e) => _spawnTick = _ticks;

        private void FightEnded(long ticksDone)
        {
            if (_counting == null || _spawnTick is not { } spawn)
                return;
            var seconds = (ticksDone - spawn) * _tickSeconds;
            _counting.Trial.Seconds += seconds;
            _counting.Fight.Seconds += seconds;
            _spawnTick = null;
        }

        private void Killed(object? sender, EnemyKilledEventArgs e)
        {
            FightEnded(_ticks + 1);
            if (_dungeon == null)
            {
                _ended = FightOutcome.Kill;
                return;
            }
            Record(e.Enemy.Id, (e.KillTick - e.SpawnTick) * _tickSeconds, FightOutcome.Kill);
        }

        private void DungeonCompleted(object? sender, DungeonEventArgs e)
        {
            _ended = FightOutcome.Kill;
            _stage = 0; // the next run of the dungeon starts at its first stage
        }

        private void PlayerDied(object? sender, PlayerDiedEventArgs e)
        {
            FightEnded(_ticks + 1);
            if (_dungeon != null)
                Record(_combat.Enemy.Enemy?.Id ?? "", (_ticks + 1 - _fightStart) * _tickSeconds, FightOutcome.Death);
            _ended = FightOutcome.Death;
        }

        private void Defended(object? sender, AttackEventArgs e) => _fightDamage += e.HealthDamage;

        // The killing blow lands before the death and kill events of the same tick, so its hit belongs to this fight.
        private void Record(string enemy, double seconds, FightOutcome outcome)
        {
            var record = new FightRecord
            {
                Trial = _result.TrialsDone,
                Stage = ++_stage,
                Enemy = enemy,
                GameSeconds = seconds,
                DamageTaken = _fightDamage,
                Outcome = outcome,
            };
            if (_tallying.RecordsRisk)
            {
                var hits = _tallying.FightHits;
                record.DamageTaken = hits.Damage;
                record.MaxHit = hits.MaxHit ?? 0;
                record.MinHp = outcome == FightOutcome.Death ? 0 : hits.MinHp;
                hits.Reset();
            }
            if (_counting != null)
            {
                var fight = _counting.Fight;
                record.Attacks = fight.Dealt.Attacks;
                record.Hits = fight.Dealt.Hits;
                record.DamageDealt = fight.Dealt.Damage;
                record.EnemyAttacks = fight.Taken.Attacks;
                record.EnemyHits = fight.Taken.Hits;
                fight.Clear();
            }
            _result.Fights.Add(record);
            _fightDamage = 0;
            _fightStart = _ticks + 1;
        }
    }
}
