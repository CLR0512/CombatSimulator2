using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class StageBreakdownTests
{
    private const string Golbin = "MelvorBase:Golbin";
    private const string Chief = "MelvorBase:GolbinChief";
    private const string Hob = "MelvorBase:Hobgolbin";

    private static FightRecord Fight(
        int trial,
        int stage,
        string enemy,
        double seconds,
        long damage,
        bool died = false,
        long? maxHit = null,
        long? minHp = null
    ) =>
        new()
        {
            Trial = trial,
            Stage = stage,
            Enemy = enemy,
            GameSeconds = seconds,
            DamageTaken = damage,
            Outcome = died ? FightOutcome.Death : FightOutcome.Kill,
            MaxHit = maxHit,
            MinHp = died && maxHit != null ? 0 : minHp,
        };

    // A dungeon result over the given fights: one batch per trial, a trial dying when its last fight did, each trial's
    // game time its fights' time plus `gap` seconds per fight.
    private static TargetResult Dungeon(int trialsDone, double gap, params FightRecord[] fights)
    {
        var result = new TargetResult
        {
            Key = "MelvorBase:HobgolbinCamp",
            Kind = TargetKind.Dungeon,
            State = TargetState.Finished,
            TrialsDone = trialsDone,
            Fights = [.. fights],
        };
        for (var t = 0; t < trialsDone; t++)
        {
            var own = fights.Where(f => f.Trial == t).ToList();
            var died = own.Any(f => f.Outcome == FightOutcome.Death);
            result.Batches.Add(
                new Tally
                {
                    Trials = 1,
                    Kills = died ? 0 : 1,
                    Deaths = died ? 1 : 0,
                    GameSeconds = own.Sum(f => f.GameSeconds) + gap * own.Count,
                }
            );
        }
        return result;
    }

    [Fact]
    public void StagesGroupTheirFightsWithEnemyCountsMostFirst()
    {
        var result = Dungeon(
            3,
            1,
            Fight(0, 1, Golbin, 10, 5),
            Fight(0, 2, Chief, 20, 30),
            Fight(1, 1, Golbin, 12, 7),
            Fight(1, 2, Hob, 18, 20),
            Fight(2, 1, Golbin, 14, 9),
            Fight(2, 2, Hob, 22, 40)
        );
        var breakdown = StageBreakdown.Of(result, 2);
        Assert.Equal(2, breakdown.Rows.Count);
        var first = breakdown.Rows[0];
        Assert.Equal(1, first.Stage);
        Assert.Equal([(Golbin, 3)], first.Enemies);
        Assert.Equal(3, first.Reached);
        Assert.Equal(12, first.MeanSeconds!.Value, 9);
        Assert.Equal(7, first.MeanDamage!.Value, 9);
        Assert.Equal(0, first.Deaths);
        var second = breakdown.Rows[1];
        Assert.Equal([(Hob, 2), (Chief, 1)], second.Enemies);
        Assert.Equal(20, second.MeanSeconds!.Value, 9);
        Assert.Equal(30, second.MeanDamage!.Value, 9);
    }

    [Fact]
    public void FightsFromTrialsNotDoneAreLeftOut()
    {
        // Trial 2 was cut off (a cancelled target's last trial): its fight isn't counted.
        var result = Dungeon(2, 0, Fight(0, 1, Golbin, 10, 5), Fight(1, 1, Golbin, 10, 5), Fight(2, 1, Golbin, 99, 99));
        var row = Assert.Single(StageBreakdown.Of(result, null).Rows);
        Assert.Equal(2, row.Reached);
        Assert.Equal(10, row.MeanSeconds!.Value, 9);
    }

    [Fact]
    public void LaterStagesCountOnlyTheTrialsThatReachedThem()
    {
        var result = Dungeon(
            4,
            0,
            Fight(0, 1, Golbin, 10, 5),
            Fight(0, 2, Chief, 20, 30),
            Fight(0, 3, Hob, 30, 50),
            Fight(1, 1, Golbin, 10, 5),
            Fight(1, 2, Chief, 20, 90, died: true),
            Fight(2, 1, Golbin, 10, 80, died: true),
            Fight(3, 1, Golbin, 10, 5),
            Fight(3, 2, Chief, 20, 30),
            Fight(3, 3, Hob, 30, 70, died: true)
        );
        var rows = StageBreakdown.Of(result, 3).Rows;
        Assert.Equal([4, 3, 2], rows.Select(r => r.Reached));
        Assert.Equal([1, 1, 1], rows.Select(r => r.Deaths));
        // The invariants: deaths add up to the dungeon's, and each stage is reached by those that passed the one before.
        Assert.Equal(result.Batches.Sum(b => b.Deaths), rows.Sum(r => r.Deaths));
        for (var s = 0; s + 1 < rows.Count; s++)
            Assert.Equal(rows[s].Reached - rows[s].Deaths, rows[s + 1].Reached);
    }

    [Fact]
    public void AStageNoTrialReachedIsListedAsNotReached()
    {
        var result = Dungeon(2, 0, Fight(0, 1, Golbin, 10, 90, died: true), Fight(1, 1, Golbin, 10, 90, died: true));
        var rows = StageBreakdown.Of(result, 3).Rows;
        Assert.Equal([1, 2, 3], rows.Select(r => r.Stage));
        Assert.Equal([2, 0, 0], rows.Select(r => r.Reached));
        Assert.Null(rows[1].MeanSeconds);
        Assert.Null(rows[1].MeanDamage);
        Assert.Empty(rows[2].Enemies);
        Assert.Null(StageBreakdown.Of(result, 3).BetweenFightsSeconds); // no completion
        // Without the stage count, only the stages seen are listed.
        Assert.Single(StageBreakdown.Of(result, null).Rows);
    }

    [Fact]
    public void EachStageHasItsOwnHighestHitAndLowestHp()
    {
        var result = Dungeon(
            3,
            0,
            Fight(0, 1, Golbin, 10, 5, maxHit: 12, minHp: 80),
            Fight(0, 2, Chief, 20, 30, maxHit: 40, minHp: 31),
            Fight(1, 1, Golbin, 10, 5, maxHit: 15, minHp: 70),
            Fight(1, 2, Chief, 20, 90, died: true, maxHit: 95),
            Fight(2, 1, Golbin, 10, 0, maxHit: 0, minHp: null) // no hit taken
        );
        var rows = StageBreakdown.Of(result, 2).Rows;
        Assert.Equal((15L, 70L), (rows[0].MaxHit, rows[0].MinHp));
        // The death's 0 isn't the stage's lowest HP: survived fights only.
        Assert.Equal((95L, 31L), (rows[1].MaxHit, rows[1].MinHp));
    }

    [Fact]
    public void FightsFromOlderRunsHaveNoStageRisk()
    {
        var result = Dungeon(2, 0, Fight(0, 1, Golbin, 10, 5), Fight(1, 1, Golbin, 12, 9));
        var row = Assert.Single(StageBreakdown.Of(result, null).Rows);
        Assert.Null(row.MaxHit);
        Assert.Null(row.MinHp);
        Assert.Equal(7, row.MeanDamage!.Value, 9);
    }

    [Fact]
    public void TimeBetweenFightsIsTheDungeonTimeLeftOverPerCompletion()
    {
        // Two completions of two fights each, 1.5 s between fights; one death in the first fight.
        var result = Dungeon(
            3,
            1.5,
            Fight(0, 1, Golbin, 10, 5),
            Fight(0, 2, Chief, 20, 30),
            Fight(1, 1, Golbin, 10, 80, died: true),
            Fight(2, 1, Golbin, 10, 5),
            Fight(2, 2, Chief, 20, 30)
        );
        // 5 fights × 1.5 s of gaps over 2 completions.
        Assert.Equal(3.75, StageBreakdown.Of(result, 2).BetweenFightsSeconds!.Value, 9);
    }

    [Fact]
    public void AnEnemyHasNoBreakdown()
    {
        var enemy = new TargetResult
        {
            Kind = TargetKind.Enemy,
            State = TargetState.Finished,
            TrialsDone = 1,
        };
        Assert.Empty(StageBreakdown.Of(enemy, null).Rows);
    }

    [Fact]
    public void AStageMeansItsFightsCountsAndCountsItsRanOverFights()
    {
        var first = Fight(0, 1, Hob, 10, 5);
        first.Attacks = 10;
        first.Hits = 4;
        first.DamageDealt = 40;
        first.EnemyAttacks = 6;
        first.EnemyHits = 2;
        var second = Fight(1, 1, Hob, 30, 5);
        second.Attacks = 20;
        second.Hits = 6;
        second.DamageDealt = 60;
        second.EnemyAttacks = 8;
        second.EnemyHits = 4;
        second.Outcome = FightOutcome.RanOver;
        var row = StageBreakdown.Of(Dungeon(2, 0, first, second), 1).Rows[0];
        Assert.Equal(new StageCounts(15, 5, 50, 7, 3), row.Counts);
        Assert.Equal(1, row.RanOver);
        Assert.Equal(0, row.Deaths);
    }

    [Fact]
    public void OlderFightsHaveNoCounts() =>
        Assert.Null(StageBreakdown.Of(Dungeon(1, 0, Fight(0, 1, Hob, 10, 5)), 1).Rows[0].Counts);
}
