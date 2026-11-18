using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class GearTextTests
{
    private static readonly Candidate[] Sets =
    [
        new(0, ChangeList.Empty, "Melee"),
        new(1, ChangeList.Parse("equip=Weapon:IronBattleaxe equip=Helmet:IronHelmet stance=Block"), "Melee"),
        new(2, ChangeList.Parse("equip=Gloves:IronGloves"), "Melee"),
        new(3, ChangeList.Parse("equip=Weapon:IronBow equip=Ammo:IronArrows stance=Rapid"), "Ranged"),
    ];

    private static Answer Answered(IReadOnlyList<StageResult> results, Goal? goal = null, bool byStyle = false)
    {
        goal ??= new Goal(Metrics.Kills);
        var answer = new Answer
        {
            Kind = SearchKind.BestGear,
            Key = new AnswerKey { Goal = goal },
            Target = Target,
            Seed = 1,
            Possible = 41472,
            RuledOut = new Dictionary<string, double> { [SearchPlan.Beaten] = 41000, [SearchPlan.Totals] = 310 },
            Simulated = 162,
            LeftOut = [new("equip=Weapon:OddStaff", Note.Text("Needs 40 Magic"))],
            Stages = [new(100, 162, 139, 23, 1.5), new(400, 23, 19, 4, 0.9), new(1000, 4, 2, 2, 1.1)],
        };
        Answers.Fill(answer, Sets, results, results.ToDictionary(r => r.Index), goal, 0.01, byStyle, Catalogue());
        return answer;
    }

    private static string[] Lines(Answer answer) => GearText.Of(answer).Split('\n');

    [Fact]
    public void AFoundSetPrintsItsGainItsSlotsAndItsHash()
    {
        var lines = Lines(Answered([Set(0, 12), Set(1, 10), Set(2, 10.02)]));
        Assert.Equal("gear Farm/Cow by kills, no deaths, margin 1%, seed 1", lines[0]);
        Assert.Equal("sets: 41,472 possible, 41,310 ruled out, 162 simulated, 1 left out", lines[1]);
        Assert.Equal("stage 1: 100 trials, 162 fought, 139 dropped, 23 left", lines[2]);
        Assert.Equal("stage 3: 1000 trials, 4 fought, 2 dropped, 2 left", lines[4]);
        Assert.StartsWith("best: +20.0% [", lines[5], StringComparison.Ordinal);
        Assert.EndsWith("] kills (300 → 360 /h), stance Block", lines[5], StringComparison.Ordinal);
        Assert.Contains("  Helmet: StoneHelmet → IronHelmet", lines);
        Assert.Contains("  Weapon: StoneSword → IronBattleaxe", lines);
        Assert.Matches("^  hash [0-9a-f]{12}$", lines[8]);
        Assert.Equal("as good (1):", lines[9]);
        Assert.StartsWith("  Gloves: nothing → IronGloves   +19.", lines[10], StringComparison.Ordinal);
        Assert.Matches("   hash [0-9a-f]{12}$", lines[10]);
    }

    [Fact]
    public void TheHashIsTheOneARunOfTheSetPrints()
    {
        var answer = Answered([Set(0, 12), Set(1, 10)]);
        Assert.Contains("  hash " + ResultsHashInput.Brief([answer.Best!.Result]), Lines(answer));
    }

    [Fact]
    public void KeepingCurrentGearSaysSoAndListsTheSetsAsGood()
    {
        var lines = Lines(Answered([Set(0, 10), Set(1, 10.02), Set(2, 14)]));
        Assert.Contains("keep what you have: no set is better by more than the margin (1 as good)", lines);
        Assert.Contains("as good (1):", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("best:", StringComparison.Ordinal));
    }

    [Fact]
    public void ASetUsingSomethingUpSaysWhatItAssumes()
    {
        var lines = Lines(Answered([Set(0, 12), Set(3, 10)]));
        Assert.Contains("  assumes IronArrows lasts", lines);
        Assert.Contains("  Ammo: nothing → IronArrows", lines);
    }

    [Fact]
    public void TheEndingsWithoutABestSayWhy()
    {
        Assert.Equal(
            "no set within the limit: closest Helmet: StoneHelmet → IronHelmet; Weapon: StoneSword → IronBattleaxe; stance Block with 1% deaths",
            Lines(Answered([Set(0, 12, deaths: 40), Set(1, 10, deaths: 3)]))[^1]
        );
        Assert.Equal(
            "no set kills this target: no result",
            Lines(Answered([Without(0, TargetState.Unfinished), Without(1, TargetState.Unfinished)]))[^1]
        );
        Assert.Equal(
            "nothing to search: nothing else owned fits an unlocked slot",
            Lines(new Answer { State = AnswerState.NothingToSearch })[^1]
        );
        Assert.Equal(
            "stopped before any set finished a stage: nothing kept",
            Lines(new Answer { State = AnswerState.Cancelled, CancelledAfter = 0 })[^1]
        );
        Assert.Equal(
            "failed: No slot Tail",
            Lines(new Answer { State = AnswerState.Failed, Reason = Note.Of("No slot {0}", "Tail") })[^1]
        );
    }

    [Fact]
    public void WhereTheLimitLetsASetDieItsDeathsAreShown()
    {
        var goal = new Goal(Metrics.Kills, DeathLimit.UnderFivePercent);
        var lines = Lines(Answered([Set(0, 12), Set(1, 10, deaths: 6)], goal));
        Assert.Equal("gear Farm/Cow by kills, under 5% deaths, margin 1%, seed 1", lines[0]);
        Assert.Contains(
            lines,
            l => l.StartsWith("best:", StringComparison.Ordinal) && l.EndsWith(", 2% deaths", StringComparison.Ordinal)
        );
        Assert.Equal("any deaths, if survived once", GearText.Deaths(DeathLimit.SurvivedOnce));
    }

    [Fact]
    public void CurrentGearThatNeverFinishesIsSaidInsteadOfAGain()
    {
        var lines = Lines(Answered([Without(0, TargetState.Unfinished), Set(1, 10)]));
        Assert.Contains("best: kills, stance Block", lines);
        Assert.Contains("  current gear: unfinished: no result", lines);
    }

    [Fact]
    public void AClimbAndARerunAndStylesAreSaid()
    {
        var answer = Answered([Set(0, 12), Set(1, 11), Set(3, 9)], byStyle: true);
        answer.Climbed = true;
        answer.Reran = true;
        answer.BehindAtStage = 1;
        answer.Seed = 77;
        answer.SkippedStyles = [new("Magic", Note.Of("switch on Spell under Also try to include it"))];
        var lines = Lines(answer);
        Assert.Contains("found a slot at a time; a better combination may exist", lines);
        Assert.Contains("the leaders were fought again with seed 77: the best was behind at stage 1", lines);
        Assert.Contains("style Magic: skipped, switch on Spell under Also try to include it", lines);
        Assert.Contains(
            lines,
            l => l.StartsWith("style Melee: Helmet: StoneHelmet → IronHelmet", StringComparison.Ordinal)
        );
        Assert.Contains(lines, l => l.StartsWith("style Ranged: Ammo: nothing → IronArrows", StringComparison.Ordinal));
        Assert.Equal("best overall: Ranged", lines[^1]);
    }

    [Fact]
    public void AStoppedSearchNamesTheStageItStoppedAt()
    {
        var answer = Answered([Set(0, 12), Set(1, 10), Set(2, 10.02)]);
        answer.StoppedAtStage = 2;
        Assert.EndsWith(", stopped at stage 2", GearText.Of(answer).Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("\nbest: ", GearText.Of(answer), StringComparison.Ordinal);
    }
}
