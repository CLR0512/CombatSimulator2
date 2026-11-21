using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class BestInSlotTextTests
{
    private static readonly AnswerKey Every = new AnswerKey().WithPool(
        SearchRequestTests.Request() with
        {
            Pool = ItemPool.Every,
        }
    );

    private static SlotChange Slot(Mark? mark, string? item = "MelvorBase:RuneHelmet") =>
        new("MelvorBase:Helmet", "Helmet", null, null, item, "Rune Helmet") { Mark = mark };

    // A guard that hides the Golbin Chief: a place the character can't enter, holding only it.
    private static readonly Spoilers Hidden = Spoilers.Of(
        [new SpoilerPlace("MelvorBase:ChiefsHut", "Chief's Hut", false, ["MelvorBase:GolbinChief"])],
        [new SpoilerEnemy("MelvorBase:GolbinChief", "Golbin Chief", false)]
    );

    [Fact]
    public void EachMarkSaysWhatTheItemTakesToGet()
    {
        Assert.Equal("  [in bank]", BestInSlotText.Mark(Slot(null), Every, Spoilers.None));
        Assert.Equal(
            "  [needs Defence level 40]",
            BestInSlotText.Mark(Slot(Mark.Locked(Note.Of("Defence level {0}", 40))), Every, Spoilers.None)
        );
        Assert.Equal(
            "  [not in your bank: Shop]",
            BestInSlotText.Mark(Slot(new Mark(MarkKind.NotInBank, null, Note.Of("Shop"))), Every, Spoilers.None)
        );
        Assert.Equal(
            "  [not in your bank: Other]  [needs Defence level 40]",
            BestInSlotText.Mark(
                Slot(new Mark(MarkKind.NotInBank, Note.Of("Defence level {0}", 40))),
                Every,
                Spoilers.None
            )
        );
    }

    [Fact]
    public void ASourceTheGuardHidesShowsAsTheGuardShowsIt()
    {
        var mark = new Mark(MarkKind.NotInBank, null, Note.Text("Golbin Chief"));
        Assert.Equal("  [not in your bank: ???]", BestInSlotText.Mark(Slot(mark), Every, Hidden));
        Assert.Equal("  [not in your bank: Golbin Chief]", BestInSlotText.Mark(Slot(mark), Every, Spoilers.None));
    }

    [Fact]
    public void OwnItemsAndEmptiedSlotsCarryNoMark()
    {
        Assert.Equal("", BestInSlotText.Mark(Slot(null), new AnswerKey(), Spoilers.None));
        Assert.Equal("", BestInSlotText.Mark(Slot(null, item: null), Every, Spoilers.None));
    }

    private static SlotDecision Decided(
        string slot,
        string? item,
        DecidedBy by,
        bool worn = false,
        Mark? mark = null,
        string? against = null,
        Estimate? difference = null
    ) => new(slot, slot, item, item, worn, mark, by, against, difference);

    [Fact]
    public void ASettledAnswerNamesEverySlotAndWhatDecidedIt()
    {
        var answer = new Answer
        {
            Key = Every,
            Best = new SetResult
            {
                Slots = [new("Gloves", "Gloves", "Kelsarin", "Kelsarin Gloves", "Blackstone", "Blackstone Gauntlet")],
            },
            Decisions =
            [
                Decided(
                    "Gloves",
                    "Blackstone Gauntlet",
                    DecidedBy.DamageTaken,
                    mark: new Mark(MarkKind.NotInBank, null, Note.Text("Blackstone Chest")),
                    against: "Kelsarin Gloves",
                    difference: new Estimate(-0.068, -0.113, -0.023, 4000, "relative")
                ),
                Decided("Cape", "Nightwing Cloak", DecidedBy.Nothing, worn: true),
                Decided("Ring", "Gold Ring", DecidedBy.Goal),
                Decided("Weapon", "Oak Bow", DecidedBy.Locked, worn: true),
            ],
            Settled = true,
        };
        var text = new System.Text.StringBuilder();
        BestInSlotText.Decisions(text, answer, Spoilers.None);
        var lines = text.ToString().Split('\n')[1..];
        Assert.Equal(
            [
                "  Gloves: Kelsarin Gloves → Blackstone Gauntlet  [not in your bank: Blackstone Chest]  least damage taken (−6.8% [−11.3, −2.3] vs Kelsarin Gloves)",
                "  Cape: keep Nightwing Cloak  [worn]  nothing separated them",
                "  Ring: keep Gold Ring  [in bank]  goal",
                "  Weapon: keep Oak Bow  [worn]  locked",
            ],
            lines
        );
    }

    [Fact]
    public void OwnItemsCarryNoMarkAndAStoppedSearchSaysItsSlotsArentSettled()
    {
        var answer = new Answer
        {
            Decisions = [Decided("Cape", "Nightwing Cloak", DecidedBy.Goal, worn: true)],
            Settled = false,
        };
        var text = new System.Text.StringBuilder();
        BestInSlotText.Decisions(text, answer, Spoilers.None);
        Assert.Equal(
            "\n  Cape: keep Nightwing Cloak  goal\n  slots not settled: the search was stopped first",
            text.ToString()
        );
    }

    [Fact]
    public void DeathsAreDecidedByTheirPlainDifferenceWhenTheWinnerNeverDies() =>
        Assert.Equal(
            "fewest deaths (−0.1 [−0.14, −0.06] vs Robe)",
            BestInSlotText.Measure(
                Decided(
                    "Body",
                    "Plate",
                    DecidedBy.Deaths,
                    against: "Robe",
                    difference: new(-0.1, -0.14, -0.06, 300, "newcombe")
                )
            )
        );

    [Fact]
    public void TheGapListsEachNewItemAloneTheFirstToGetFirst()
    {
        var answer = new Answer
        {
            Best = new SetResult { Percent = new Estimate(0.388, 0.347, 0.43, 30, "relative") },
            Gap =
            [
                new(
                    [new("a", "Dracolith Spitfire Amulet", null)],
                    new Estimate(0.21, 0.182, 0.239, 30, "relative"),
                    0.54,
                    false
                ),
                new(
                    [new("w", "Kelsarin Bow", null), new("q", "Spectre Arrows", null)],
                    new Estimate(0.041, 0.02, 0.063, 30, "relative"),
                    0.1,
                    false
                ),
                new(
                    [new("c", "Nightwing Cloak", null)],
                    new Estimate(0.004, -0.006, 0.014, 30, "relative"),
                    0.01,
                    true
                ),
            ],
            GapFought = true,
        };
        var text = new System.Text.StringBuilder();
        BestInSlotText.Gap(text, answer);
        Assert.Equal(
            [
                "",
                "gap: +38.8% [34.7, 43.0] from now",
                "  get first: Dracolith Spitfire Amulet  +21.0% [18.2, 23.9]  (54% of the gap)",
                "  Kelsarin Bow + Spectre Arrows  +4.1% [2.0, 6.3]",
                "  Nightwing Cloak  +0.4% [−0.6, 1.4]  only with the rest",
            ],
            text.ToString().Split('\n')
        );
        var stopped = new System.Text.StringBuilder();
        BestInSlotText.Gap(stopped, new Answer { GapFought = false });
        Assert.Equal("\ngap: not fought, the search was stopped first", stopped.ToString());
    }
}
