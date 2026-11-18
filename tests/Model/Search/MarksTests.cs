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
