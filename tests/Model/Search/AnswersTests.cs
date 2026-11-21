using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class AnswersTests
{
    private static readonly Goal Kills = new(Metrics.Kills);

    private static readonly Candidate[] Sets =
    [
        new(0, ChangeList.Empty, "Melee"),
        new(1, ChangeList.Parse("equip=Weapon:IronBattleaxe equip=Helmet:IronHelmet stance=Block"), "Melee")
        {
            Same = [ChangeList.Parse("equip=Weapon:IronBattleaxe equip=Helmet:SteelHelmet stance=Block")],
        },
        new(2, ChangeList.Parse("equip=Gloves:IronGloves"), "Melee"),
        new(3, ChangeList.Parse("equip=Weapon:IronBow equip=Ammo:IronArrows stance=Rapid"), "Ranged"),
    ];

    private static Answer Filled(
        IReadOnlyList<StageResult> results,
        bool byStyle = false,
        Goal? goal = null,
        double share = 0.01
    )
    {
        var answer = new Answer();
        Answers.Fill(
            answer,
            Sets,
            results,
            results.ToDictionary(r => r.Index),
            goal ?? Kills,
            share,
            byStyle,
            Catalogue()
        );
        return answer;
    }

    [Theory]
    [InlineData(1000, new[] { 100, 400, 1000 })]
    [InlineData(400, new[] { 100, 400 })]
    [InlineData(300, new[] { 100, 300 })]
    [InlineData(100, new[] { 100 })]
    [InlineData(30, new[] { 30 })]
    [InlineData(5000, new[] { 100, 400, 5000 })]
    public void AStageIsSkippedWhenNotLargerThanTheOneBefore(int trials, int[] stages) =>
        Assert.Equal(stages, Answers.Stages(trials));

    [Theory]
    [InlineData(1000, 99, new[] { 100, 400, 1000 })]
    [InlineData(1000, 100, new[] { 25, 100, 400, 1000 })]
    [InlineData(100, 5000, new[] { 25, 100 })]
    [InlineData(20, 5000, new[] { 20 })]
    public void ABigRoundStartsWithAQuarterStage(int trials, int sets, int[] stages) =>
        Assert.Equal(stages, Answers.Stages(trials, sets));

    [Fact]
    public void XpInTheMainSkillIsTheSetsOwn()
    {
        Assert.Equal("xp:MelvorBase:Ranged", Answers.MetricFor(new Goal(Metrics.MainXp), "MelvorBase:Ranged"));
        Assert.Equal(Metrics.Kills, Answers.MetricFor(new Goal(Metrics.MainXp), null));
        Assert.Equal(Metrics.KillTime, Answers.MetricFor(new Goal(Metrics.KillTime), "MelvorBase:Ranged"));
    }

    [Fact]
    public void XpInTheMainSkillIsCurrentGearsWithinItsStyle()
    {
        var goal = new Goal(Metrics.MainXp);
        const string Attack = "MelvorBase:Attack";
        const string Strength = "MelvorBase:Strength";
        Assert.Equal("xp:" + Strength, Answers.MetricFor(goal, Attack, "melee", Strength, "melee"));
        Assert.Equal("xp:MelvorBase:Ranged", Answers.MetricFor(goal, "MelvorBase:Ranged", "ranged", Strength, "melee"));
        Assert.Equal("xp:" + Attack, Answers.MetricFor(goal, Attack, "melee", null, "melee"));
        Assert.Equal(
            Metrics.KillTime,
            Answers.MetricFor(new Goal(Metrics.KillTime), Attack, "melee", Strength, "melee")
        );
    }

    [Fact]
    public void AFoundSetShowsEachChangedSlotItsStanceAndItsGain()
    {
        var answer = Filled([Set(0, 12), Set(1, 10), Set(2, 11.5)]);
        Assert.Equal(AnswerState.Found, answer.State);
        var best = answer.Best!;
        Assert.Equal(
            ["Helmet: StoneHelmet → IronHelmet", "Weapon: StoneSword → IronBattleaxe"],
            best.Slots.Select(s => $"{s.SlotName}: {s.OldName} → {s.NewName}").Order()
        );
        Assert.Equal(Id("Block"), best.Stance);
        Assert.Equal("Melee", best.Style);
        Assert.True(best.Gain!.Low > 0);
        Assert.Equal(0.2, best.Percent!.Point, 2);
        Assert.Same(answer.Gain, best.Gain);
        Assert.Single(best.Same);
        Assert.Empty(answer.Ties);
        Assert.Null(answer.Current!.Gain);
        Assert.Empty(answer.Current.Slots);
        Assert.Equal(0.01, answer.Margin);
        Assert.Equal(Metrics.Kills, answer.Metric);
    }

    [Fact]
    public void AnEmptySlotFilledHasNoOldItem()
    {
        var gloves = Filled([Set(0, 12), Set(2, 10)]).Best!.Slots.Single();
        Assert.Null(gloves.Old);
        Assert.Null(gloves.OldName);
        Assert.Equal("IronGloves", gloves.NewName);
    }

    [Fact]
    public void ASetUsingSomethingUpNamesIt()
    {
        var answer = Filled([Set(0, 12), Set(3, 10)]);
        Assert.Equal(["IronArrows"], answer.Best!.Assumes);
    }

    [Fact]
    public void KeepingCurrentGearHasNoBestAndListsTheSetsAsGood()
    {
        var answer = Filled([Set(0, 10), Set(1, 10.02), Set(2, 14)]);
        Assert.Equal(AnswerState.KeepCurrent, answer.State);
        Assert.Null(answer.Best);
        Assert.Null(answer.Gain);
        var tie = Assert.Single(answer.Ties);
        Assert.Equal(Sets[1].Changes, tie.Changes);
        Assert.NotNull(tie.FromBest);
    }

    [Fact]
    public void WithNoSetWithinTheLimitTheClosestIsShownEvenIfItLeftEarlier()
    {
        // Set 1 was dropped at an earlier stage with three deaths; the sets still in died more.
        var answer = new Answer();
        var stillIn = new[] { Set(0, 12, deaths: 40), Set(2, 9, deaths: 60) };
        var all = stillIn.Append(Set(1, 10, deaths: 3, trials: 90)).ToDictionary(r => r.Index);
        Answers.Fill(answer, Sets, stillIn, all, Kills, 0.01, false, Catalogue());
        Assert.Equal(AnswerState.NoneWithinLimit, answer.State);
        Assert.Null(answer.Best);
        Assert.Equal(Sets[1].Changes, answer.Closest!.Changes);
    }

    [Fact]
    public void WithNoSetFinishedTheReasonIsCurrentGears()
    {
        var answer = Filled([Without(0, TargetState.Unfinished), Without(1, TargetState.Unfinished)]);
        Assert.Equal(AnswerState.NoneKills, answer.State);
        Assert.Equal("no result", answer.Reason!.English());
    }

    [Fact]
    public void EachStyleHasItsOwnBest()
    {
        var answer = Filled([Set(0, 12), Set(1, 11), Set(2, 11.8), Set(3, 9)], byStyle: true);
        Assert.Equal(AnswerState.Found, answer.State);
        Assert.Equal("Ranged", answer.Best!.Style);
        Assert.Equal(["Melee", "Ranged"], answer.ByStyle.Select(s => s.Style));
        Assert.Equal(Sets[1].Changes, answer.ByStyle[0].Best!.Changes);
        Assert.Equal(Sets[3].Changes, answer.ByStyle[1].Best!.Changes);
        Assert.Empty(Filled([Set(0, 12), Set(1, 11)]).ByStyle);
    }

    [Fact]
    public void EachChangedSlotIsMarkedByWhatItsItemTakesToGet()
    {
        var catalogue = SearchFixture.Catalogue(i =>
            i.Id == SearchFixture.Id("RuneShield") ? i with { Source = Note.Text("Golbin Chief") } : i
        );
        var candidate = new Candidate(
            1,
            ChangeList.Parse("equip=Offhand:RuneShield equip=Helmet:DragonHelmet equip=Platebody:IronPlatebody"),
            "Melee"
        );
        var result = new StageResult(1, RankingTests.Finished("t", "t", 20), "kills");
        var set = Answers.Set(candidate, result, null, catalogue);
        Mark? Of(string slot) => set.Slots.Single(s => s.Slot == SearchFixture.Id(slot)).Mark;
        // Not owned, wearable: where it comes from.
        Assert.Equal(MarkKind.NotInBank, Of("Offhand")!.Kind);
        Assert.Equal("Golbin Chief", Of("Offhand")!.Source!.English());
        Assert.Null(Of("Offhand")!.Needs);
        // Not owned and lacking a level: both, and the set is fought with requirements ignored.
        Assert.Equal(MarkKind.NotInBank, Of("Helmet")!.Kind);
        Assert.Equal("Defence level 60", Of("Helmet")!.Needs!.English());
        Assert.True(set.NeedsRequirementsIgnored);
        // Owned and wearable: no mark, as before.
        Assert.Null(Of("Platebody"));
    }
}
