using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class StallReasonTests
{
    private static Stall With(
        long attacks,
        long hits,
        long attack = 0,
        long healed = 0,
        long armourCut = 0,
        bool immune = false,
        long hpLeft = 50,
        long hpMax = 100
    ) =>
        new()
        {
            Trials = 1,
            Counts = new FightCounts
            {
                Dealt = new SideCounts
                {
                    Attacks = attacks,
                    Hits = hits,
                    Attack = attack,
                    Healed = healed,
                    ArmourCut = armourCut,
                },
            },
            Immune = immune,
            HpLeft = hpLeft,
            HpMax = hpMax,
            LimitSeconds = 300,
        };

    [Fact]
    public void EachReasonHasItsTest()
    {
        Assert.Equal(StallReason.NoAttack, StallReasons.Of(With(0, 0)));
        Assert.Equal(StallReason.Immune, StallReasons.Of(With(10, 0, immune: true)));
        Assert.Equal(StallReason.NeverHit, StallReasons.Of(With(10, 0)));
        Assert.Equal(StallReason.HealsBack, StallReasons.Of(With(10, 5, attack: 100, healed: 50)));
        Assert.Equal(StallReason.Armour, StallReasons.Of(With(10, 5, attack: 5, armourCut: 6)));
        Assert.Equal(StallReason.TooSlow, StallReasons.Of(With(10, 5, attack: 100, healed: 49, armourCut: 100)));
    }

    [Fact]
    public void NeighbouringReasonsTakeTheirOrder()
    {
        // No attack before immune, immune before never hit, heals before armour, armour before too slow.
        Assert.Equal(StallReason.NoAttack, StallReasons.Of(With(0, 0, immune: true)));
        Assert.Equal(StallReason.Immune, StallReasons.Of(With(10, 0, immune: true, healed: 5)));
        Assert.Equal(StallReason.NeverHit, StallReasons.Of(With(10, 0, healed: 5, armourCut: 5)));
        Assert.Equal(StallReason.HealsBack, StallReasons.Of(With(10, 5, attack: 5, healed: 5, armourCut: 500)));
        Assert.Equal(StallReason.Armour, StallReasons.Of(With(10, 5, attack: 5, healed: 2, armourCut: 6)));
        Assert.Equal(StallReason.TooSlow, StallReasons.Of(With(10, 5, attack: 5, armourCut: 5)));
    }

    [Fact]
    public void HealingWithNothingDealtIsNotHealsBack()
    {
        Assert.Equal(StallReason.Armour, StallReasons.Of(With(10, 5, attack: 0, healed: 3, armourCut: 1)));
    }

    [Fact]
    public void TheProbesStallsGiveTheirReasons()
    {
        var expected = new Dictionary<string, StallReason>
        {
            ["QuestPointyPreparation/ArcheryTarget"] = StallReason.Immune,
            ["MistyThicket/OakTreant"] = StallReason.Armour,
            ["QuestRockyRumble/GrumpyGolem"] = StallReason.Armour,
            ["ToweringDivide/Wolf"] = StallReason.HealsBack,
            ["SlimeForest/RainbowSlime"] = StallReason.HealsBack,
            ["Murkwoods/MurkwoodTrapper"] = StallReason.TooSlow,
            ["MistyThicket/WillOWisp"] = StallReason.TooSlow,
            ["ToxicBog/Plaguewing"] = StallReason.TooSlow,
        };
        var stalls = ProbeStalls();
        Assert.Equal(expected.Keys.Order(), stalls.Keys.Order());
        foreach (var (key, reason) in expected)
            Assert.True(reason == StallReasons.Of(stalls[key]), $"{key}: {StallReasons.Of(stalls[key])}");
    }

    [Fact]
    public void EachNoteSaysItsNumbers()
    {
        var stalls = ProbeStalls();
        Assert.Equal("Immune to your attacks: 0 of 110 landed.", Note("QuestPointyPreparation/ArcheryTarget"));
        Assert.Equal("Armour stops your hits: 48 hits did 48 damage, 87% HP left.", Note("MistyThicket/OakTreant"));
        Assert.Equal("It heals back what you deal: 273 dealt, 273 healed, 100% HP left.", Note("ToweringDivide/Wolf"));
        Assert.Equal(
            "Too slow: 27% HP left after 5 minutes. 18 of 108 attacks landed, 13 damage a hit.",
            Note("Murkwoods/MurkwoodTrapper")
        );
        Assert.Equal("You never attacked in 5 minutes.", StallReasons.Note(With(0, 0)).English());
        Assert.Equal("Never hit it: 0 of 10 attacks landed.", StallReasons.Note(With(10, 0)).English());
        string Note(string key) => StallReasons.Note(stalls[key]).English();
    }

    [Fact]
    public void ADungeonsNoteStartsWithItsStageAndEnemy()
    {
        var stall = With(10, 0);
        stall.Stage = 2;
        stall.Enemy = "MelvorBase:ToxicSludge";
        Assert.Equal(
            "Stage 2, Toxic Sludge: Never hit it: 0 of 10 attacks landed.",
            StallReasons.Note(stall, "Toxic Sludge").English()
        );
        Assert.StartsWith("Stage 2, MelvorBase:ToxicSludge: ", StallReasons.Note(stall).English());
    }

    [Fact]
    public void HeadingsCarryTheirCount()
    {
        Assert.Equal("Armour stops your hits (3)", StallReasons.Heading(StallReason.Armour, 3).English());
        Assert.Equal("Too slow (1)", StallReasons.Heading(StallReason.TooSlow, 1).English());
        Assert.Equal(
            Enum.GetValues<StallReason>().Length,
            Enum.GetValues<StallReason>().Select(StallReasons.Word).Distinct().Count()
        );
    }

    /// <summary>The probe's counts for eight stalled targets, as stalls.</summary>
    internal static Dictionary<string, Stall> ProbeStalls()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestPaths.Fixtures("stalls.json")));
        var stalls = new Dictionary<string, Stall>();
        foreach (var t in doc.RootElement.GetProperty("targets").EnumerateArray())
            stalls[t.GetProperty("key").GetString()!] = new Stall
            {
                Trials = 1,
                Counts = new FightCounts
                {
                    Dealt = t.GetProperty("dealt").Deserialize<SideCounts>(RunFile.Options)!,
                    Taken = t.GetProperty("taken").Deserialize<SideCounts>(RunFile.Options)!,
                },
                Immune = t.GetProperty("immune").GetBoolean(),
                HpLeft = t.GetProperty("hpLeft").GetInt64(),
                HpMax = t.GetProperty("hpMax").GetInt64(),
                WardLeft = t.GetProperty("wardLeft").GetInt64(),
                LimitSeconds = 300,
            };
        return stalls;
    }

    private static List<Tally> Batches(int count, int ranOver) =>
        [.. Enumerable.Range(0, count).Select(_ => new Tally { Trials = 33, RanOver = ranOver })];

    [Fact]
    public void TrialsRunOverTooOftenOnlyWhenTheWholeRangeIsAtFivePercent()
    {
        Assert.False(StallReasons.TooOftenOver(Batches(2, 33)));
        Assert.True(StallReasons.TooOftenOver(Batches(3, 25)));
        Assert.False(StallReasons.TooOftenOver(Batches(3, 2)));
        Assert.False(StallReasons.TooOftenOver(Batches(30, 1)));
        Assert.False(
            StallReasons.TooOftenOver([
                .. Batches(3, 0)
                    .Select(b =>
                    {
                        b.RanOver = null;
                        return b;
                    }),
            ])
        );
    }

    [Fact]
    public void AGivenUpTargetSaysHowOftenThenWhy()
    {
        var stall = With(10, 5, attack: 50);
        Assert.Equal(
            "25 of 99 trials ran over 5 minutes. Too slow: 50% HP left after 5 minutes. 5 of 10 attacks landed, 10 damage a hit.",
            StallReasons.GaveUp(stall, 25, 99).English()
        );
    }
}
