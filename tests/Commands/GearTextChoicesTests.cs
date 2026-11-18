using System.Text;
using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class GearTextChoicesTests
{
    private static Estimate E(double point) => new(point, point, point, 30, "ratio");

    [Fact]
    public void AGearAnswerHasNoChoiceLines()
    {
        var text = GearText.Of(AnswerTests.Sample());
        Assert.DoesNotContain(", for ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Prayers:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cheaper:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASetupAnswerNamesTheLoadoutAndEachChoice()
    {
        var answer = AnswerStoreFormat2Tests.SetupAnswer();
        answer.Best!.Choices[0].Supplies =
        [
            new SupplyCost("used:currency:MelvorBase:PrayerPoints", E(1240)) { Name = Note.Of("Prayer points") },
        ];
        var text = GearText.Of(answer);
        Assert.Contains(", for Loadout 2", text, StringComparison.Ordinal);
        Assert.Contains(
            "\n  Prayers: none → Rock Skin, Sharp Eye  [needs Prayer level 40]   prayer points 1,240 /h",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains("\n  Utility 1: none → Bone Casing  [not in your bank]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AChoiceKeptSaysKeep()
    {
        var text = new StringBuilder();
        GearText.Choices(
            text,
            new SetResult { Choices = [new ChoiceChange { Kind = ChoiceKind.Imbuement, Name = "imbuement" }] }
        );
        Assert.Equal("\n  Imbuement: keep", text.ToString());
    }

    [Fact]
    public void CheaperSaysWhatItGivesUpAndSaves()
    {
        var answer = AnswerStoreFormat2Tests.SetupAnswer();
        var points = "used:currency:MelvorBase:PrayerPoints";
        answer.Best!.Supplies = [new SupplyCost(points, E(1240)) { Name = Note.Of("Prayer points") }];
        answer.Best.Percent = E(0.182);
        var cheaper = AnswerTests.Sample().Best!;
        cheaper.Percent = E(0.176);
        cheaper.Supplies = [new SupplyCost(points, E(820))];
        answer.Alternatives = new Alternatives { Cheaper = cheaper };
        var text = GearText.Of(answer);
        Assert.Contains("\ncheaper: −0.6% · 420 fewer prayer points /h", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanCountsEachKind()
    {
        var plan = new PlanPreview
        {
            Possible = 10,
            ToFight = 4,
            Kinds =
            [
                new KindCount(ChoiceKind.Prayers, "prayers", 19, 6, 2),
                new KindCount(ChoiceKind.Potion, "potion", 0, 0, 1),
            ],
            On = [ChoiceKind.Prayers, ChoiceKind.Potion],
            Seconds = 200,
            KindsOverLimit = true,
            LimitSeconds = 150,
        };
        var text = GearPlanText.Of(plan);
        Assert.Contains("\nprayers: 19 to try, 2 slots, 6 pairs at most", text, StringComparison.Ordinal);
        Assert.Contains("\npotion: 0 to try: no combat potions in this build", text, StringComparison.Ordinal);
        Assert.Contains("\nover the limit: longer than 150 s", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPotionToTryIsTheCharactersWhenTheBuildHasSome()
    {
        var plan = new PlanPreview
        {
            Kinds = [new KindCount(ChoiceKind.Potion, "potion", 0, 0, 1)],
            On = [ChoiceKind.Potion],
            PotionsInBuild = true,
        };
        var text = GearPlanText.Of(plan);
        Assert.Contains("\npotion: 0 to try: you have no combat potions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("in this build", text, StringComparison.Ordinal);
    }
}
