using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MarksTests
{
    private static ChoiceOption Option(bool owned = true, params Note[] unmet) =>
        new(ChoiceKind.Food, ["MelvorBase:Shark"], ChangeList.Parse("food=Shark")) { Owned = owned, Unmet = unmet };

    [Fact]
    public void WhatTheCharacterHasHasNoMark() => Assert.Equal(Mark.None, Mark.Of(Option()));

    [Fact]
    public void AnItemTheBankLacksIsNotInTheBank() => Assert.Equal(Mark.NotInBank, Mark.Of(Option(owned: false)));

    [Fact]
    public void ALockedOptionSaysWhatItNeedsBeforeTheBank()
    {
        var mark = Mark.Of(Option(false, Note.Of("Prayer level {0}", 40)));
        Assert.Equal(MarkKind.Locked, mark.Kind);
        Assert.Equal("Prayer level 40", mark.Needs!.English());
    }

    private static GearItem Item(int owned, Note? source = null, params Note[] unmet) =>
        new("MelvorBase:RuneHelmet", "Rune Helmet", "Helmet")
        {
            Owned = owned,
            Source = source,
            Unmet = unmet,
        };

    [Fact]
    public void AnOwnedWearableItemHasNoMark() => Assert.Equal(Mark.None, Mark.OfItem(Item(1, Note.Text("Golbin"))));

    [Fact]
    public void AnOwnedItemTheLevelsDontMeetNeedsThemAndNamesNoSource()
    {
        var mark = Mark.OfItem(Item(1, Note.Text("Golbin"), Note.Of("Defence level {0}", 40)));
        Assert.Equal(MarkKind.Locked, mark.Kind);
        Assert.Equal("Defence level 40", mark.Needs!.English());
        Assert.Null(mark.Source);
        Assert.True(mark.HasNeeds);
    }

    [Fact]
    public void AnUnownedItemNamesWhereItComesFrom()
    {
        var mark = Mark.OfItem(Item(0, Note.Of("Shop")));
        Assert.Equal(MarkKind.NotInBank, mark.Kind);
        Assert.Equal("Shop", mark.Source!.English());
        Assert.Null(mark.Needs);
        Assert.False(mark.HasNeeds);
    }

    [Fact]
    public void AnUnownedItemLackingLevelsIsNotInTheBankWithBoth()
    {
        var mark = Mark.OfItem(
            Item(0, Note.Text("Golbin"), Note.Of("Defence level {0}", 40), Note.Of("Attack level {0}", 30))
        );
        Assert.Equal(MarkKind.NotInBank, mark.Kind);
        Assert.Equal("Defence level 40", mark.Needs!.English());
        Assert.Equal("Golbin", mark.Source!.English());
        Assert.True(mark.HasNeeds);
    }

    [Fact]
    public void AnUnownedItemWithoutASourceHasNone() => Assert.Null(Mark.OfItem(Item(0)).Source);

    [Fact]
    public void AMarkWithoutASourceSavesAsBefore()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Mark.Locked(Note.Text("Attack 60")), RunFile.Options);
        Assert.DoesNotContain("source", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hasNeeds", json, StringComparison.Ordinal);
        var back = System.Text.Json.JsonSerializer.Deserialize<Mark>(
            System.Text.Json.JsonSerializer.Serialize(Mark.OfItem(Item(0, Note.Of("Shop"))), RunFile.Options),
            RunFile.Options
        );
        Assert.Equal("Shop", back!.Source!.English());
    }

    [Fact]
    public void ASetHoldingAnUnownedItemThatLacksLevelsNeedsRequirementsIgnored()
    {
        static SetResult Helmet(GearItem item) =>
            new() { Slots = [new("Helmet", "Helmet", null, null, item.Id, item.Name) { Mark = Mark.OfItem(item) }] };
        Assert.True(Helmet(Item(0, null, Note.Of("Defence level {0}", 40))).NeedsRequirementsIgnored);
        Assert.False(Helmet(Item(0)).NeedsRequirementsIgnored);
    }

    [Fact]
    public void AnAnswerMarksTheChoiceItChangesNotTheOnesItKeeps()
    {
        var shark = Option(owned: false);
        var food = new Choice(ChoiceKind.Food, null, new ChoiceOption(ChoiceKind.Food, [], ChangeList.Empty), [shark]);
        var changed = new SetResult { Changes = ChangeList.Parse("food=Shark") };
        SetupAnswers.Describe(changed, [food], _ => false);
        Assert.Equal(MarkKind.NotInBank, changed.Choices[0].Mark.Kind);
        var kept = new SetResult();
        SetupAnswers.Describe(kept, [food], _ => false);
        Assert.Equal(MarkKind.None, kept.Choices[0].Mark.Kind);
        Assert.True(kept.Choices[0].Keep);
    }

    [Fact]
    public void OnlyASetHoldingALockedChoiceNeedsRequirementsIgnored()
    {
        static SetResult Shark(ChoiceOption shark)
        {
            var food = new Choice(
                ChoiceKind.Food,
                null,
                new ChoiceOption(ChoiceKind.Food, [], ChangeList.Empty),
                [shark]
            );
            var set = new SetResult { Changes = ChangeList.Parse("food=Shark") };
            SetupAnswers.Describe(set, [food], _ => false);
            return set;
        }
        Assert.False(Shark(Option(owned: false)).NeedsRequirementsIgnored);
        Assert.True(Shark(Option(true, Note.Of("Prayer level {0}", 40))).NeedsRequirementsIgnored);
        var slot = new SetResult
        {
            Slots =
            [
                new("Weapon", "Weapon", null, null, "MelvorBase:A", "A") { Mark = Mark.Locked(Note.Text("Attack 60")) },
            ],
        };
        Assert.True(slot.NeedsRequirementsIgnored);
    }
}
