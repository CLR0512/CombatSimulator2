using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class DroptimizerTests
{
    private static readonly Goal Kills = new(Metrics.Kills);

    private static SearchRequest Request() => SearchRequestTests.Request(SearchKind.Droptimizer);

    // The fixture's unowned items, with two more helmets so one can beat the other.
    private static GearCatalogue WithHelmets() =>
        Catalogue() with
        {
            Items =
            [
                .. Catalogue().Items,
                new GearItem(Id("RuneHelmet"), "RuneHelmet", "Helmet")
                {
                    Stats = new Dictionary<string, double> { [Defence] = 30 },
                    Source = Note.Text("Hobgoblin Camp"),
                },
                new GearItem(Id("AdamantHelmet"), "AdamantHelmet", "Helmet")
                {
                    Stats = new Dictionary<string, double> { [Defence] = 20 },
                },
            ],
        };

    [Fact]
    public void EachUnownedWearableItemIsSwappedAloneIntoCurrentGear()
    {
        var plan = Droptimizer.Plan(Catalogue(), Request(), comparing: false);
        Assert.True(plan.Candidates[0].IsCurrent);
        Assert.Equal(
            ["equip=Weapon:RuneSword stance=Slash", "equip=Offhand:RuneShield"],
            plan.Candidates.Skip(1).Select(c => c.Changes.ShortForm)
        );
        Assert.Equal(new DropItem(Id("RuneSword"), Id("Weapon")), plan.Items[1]);
        Assert.Equal(3, plan.Unowned);
        Assert.Equal(2, plan.Wearable);
        Assert.Equal(0, plan.RuledOut);
        // Never an owned item, never two unowned ones at once.
        Assert.All(plan.Candidates, c => Assert.True(c.Changes.Count(x => x.Kind == ChangeKind.Equip) <= 1));
    }

    [Fact]
    public void AnItemNotYetWearableIsListedWithWhatItIsMissing()
    {
        var plan = Droptimizer.Plan(Catalogue(), Request(), comparing: false);
        var notYet = Assert.Single(plan.NotYetWearable);
        Assert.Equal(Id("DragonHelmet"), notYet.Item);
        Assert.Equal(["Defence level 60"], notYet.Missing.Select(n => n.English()));
        Assert.Equal("Other", notYet.Source.English());
        Assert.DoesNotContain(plan.Candidates, c => c.Changes.Any(x => x.Value == Id("DragonHelmet")));
    }

    [Fact]
    public void AnExcludedItemIsNeitherFoughtNorListed()
    {
        var request = Request() with
        {
            Excluded = new HashSet<string> { Id("RuneSword"), Id("DragonHelmet") },
        };
        var plan = Droptimizer.Plan(Catalogue(), request, comparing: false);
        Assert.Equal(["equip=Offhand:RuneShield"], plan.Candidates.Skip(1).Select(c => c.Changes.ShortForm));
        Assert.Empty(plan.NotYetWearable);
        Assert.Equal(1, plan.Unowned);
    }

    [Fact]
    public void AWeaponFightsInTheStanceFoughtInNowWhenItHasItElseItsFirst()
    {
        var bow = Catalogue(i => i.Id == Id("IronBow") ? i with { Owned = 0 } : i);
        var plan = Droptimizer.Plan(bow, Request(), comparing: false);
        Assert.Contains(plan.Candidates, c => c.Changes.ShortForm == "equip=Weapon:IronBow stance=Accurate");
        Assert.Contains(plan.Candidates, c => c.Changes.ShortForm == "equip=Weapon:RuneSword stance=Slash");
        Assert.Equal("Ranged", plan.Candidates.Single(c => c.Changes.Any(x => x.Value == Id("IronBow"))).Style);
    }

    [Fact]
    public void AnItemGoesInAnEmptySlotOfItsTypeFirst()
    {
        var ring = Catalogue(i =>
            i.Id == Id("SilverRing") ? i with { Owned = 0 }
            : i.Id == Id("GoldRing") ? i with { Worn = Id("Ring1") }
            : i
        );
        var plan = Droptimizer.Plan(ring, Request(), comparing: false);
        Assert.Contains(plan.Candidates, c => c.Changes.ShortForm == "equip=Ring2:SilverRing");
    }

    [Fact]
    public void WithComparingAnItemAnotherUnownedOneBeatsIsLeftOut()
    {
        var plan = Droptimizer.Plan(WithHelmets(), Request(), comparing: true);
        Assert.Contains(plan.Candidates, c => c.Changes.Any(x => x.Value == Id("RuneHelmet")));
        Assert.DoesNotContain(plan.Candidates, c => c.Changes.Any(x => x.Value == Id("AdamantHelmet")));
        Assert.Equal(1, plan.RuledOut);
        Assert.Equal(4, plan.Wearable);
        // A weapon is never left out this way: it fights in a stance of its own.
        Assert.Contains(plan.Candidates, c => c.Changes.Any(x => x.Value == Id("RuneSword")));
        var all = Droptimizer.Plan(WithHelmets(), Request(), comparing: false);
        Assert.Contains(all.Candidates, c => c.Changes.Any(x => x.Value == Id("AdamantHelmet")));
    }

    [Fact]
    public void ACandidateThatCannotBeAGainAnyMoreIsDropped()
    {
        // Current gear kills every 12 s. One swap is clearly better, one is no better than the margin, one worse.
        var step = Droptimizer.After([Set(0, 12), Set(1, 10), Set(2, 11.95), Set(3, 14)], Kills, 0.01);
        Assert.Equal([0, 1], step.In);
        Assert.Equal(DropReason.Behind, step.Dropped[2]);
        Assert.Equal(DropReason.Behind, step.Dropped[3]);
    }

    [Fact]
    public void ASwapOverTheDeathLimitOrWithoutAResultIsDropped()
    {
        var step = Droptimizer.After(
            [Set(0, 12), Set(1, 8, deaths: 1), Without(2, TargetState.Refused), Without(3, TargetState.Unfinished)],
            Kills,
            0.01
        );
        Assert.Equal(DropReason.OverLimit, step.Dropped[1]);
        Assert.Equal(DropReason.Refused, step.Dropped[2]);
        Assert.Equal(DropReason.Unfinished, step.Dropped[3]);
        Assert.Equal([0], step.In);
    }

    [Fact]
    public void AGainHasItsWholeRangeAboveTheMargin()
    {
        var current = Set(0, 12);
        Assert.True(Droptimizer.IsGain(current, Set(1, 10), Kills, 0.01));
        // Half a percent better, moving with current gear: clearly better, yet inside the margin.
        Assert.False(Droptimizer.IsGain(current, Set(2, 11.94), Kills, 0.01));
        Assert.True(Droptimizer.IsGain(current, Set(2, 11.94), Kills, 0));
        Assert.False(Droptimizer.IsGain(current, Set(3, 14), Kills, 0.01));
        Assert.False(Droptimizer.IsGain(current, Set(4, 8, deaths: 1), Kills, 0.01));
        // Current gear that finishes no fight: any swap that does, within the limit, is a gain.
        Assert.True(Droptimizer.IsGain(Without(0, TargetState.Unfinished), Set(1, 10), Kills, 0.01));
        Assert.False(Droptimizer.IsGain(Without(0, TargetState.Unfinished), Set(4, 8, deaths: 1), Kills, 0.01));
        Assert.False(
            Droptimizer.IsGain(Without(0, TargetState.Unfinished), Without(5, TargetState.Unfinished), Kills, 0.01)
        );
    }

    [Fact]
    public void WithoutAValueForCurrentGearEveryCandidateInsideTheLimitGoesOn()
    {
        var step = Droptimizer.After([Without(0, TargetState.Unfinished), Set(1, 10), Set(2, 30)], Kills, 0.01);
        Assert.Equal([0, 1, 2], step.In);
    }

    [Fact]
    public void AnItemAsGoodThatTakesClearlyLessDamageIsSafer()
    {
        // Current gear kills every 10 s and takes 25 damage a second.
        var current = Taking(Set(0, 10), 25);
        Assert.True(Droptimizer.IsSafer(current, Taking(Set(1, 10), 20), Kills, 0.01));
        // Barely less damage, or more: not safer.
        Assert.False(Droptimizer.IsSafer(current, Taking(Set(1, 10), 24.9), Kills, 0.01));
        // Safer but clearly slower: not at no cost.
        Assert.False(Droptimizer.IsSafer(current, Taking(Set(1, 12), 20), Kills, 0.01));
        // Without damage counts nothing is safer.
        Assert.False(Droptimizer.IsSafer(Set(0, 10), Set(1, 10), Kills, 0.01));
    }

    [Fact]
    public void ASaferItemStaysInTheRace()
    {
        var step = Droptimizer.After(
            [Taking(Set(0, 10), 25), Taking(Set(1, 10), 20), Taking(Set(2, 10), 25)],
            Kills,
            0.01
        );
        Assert.Equal([0, 1], step.In);
        Assert.Equal(DropReason.Behind, step.Dropped[2]);
    }

    [Fact]
    public void TheAnswerListsGainsAndTheSaferApart()
    {
        // The Rune Sword is quicker; the Rune Shield only spares damage.
        var catalogue = Catalogue();
        var plan = Droptimizer.Plan(catalogue, Request(), comparing: false);
        var sword = plan.Items.Single(p => p.Value.Item == Id("RuneSword")).Key;
        var shield = plan.Items.Single(p => p.Value.Item == Id("RuneShield")).Key;
        var answer = new Answer();
        Answers.FillDrops(
            answer,
            plan,
            [Taking(Set(0, 10), 25), Taking(Set(sword, 8), 25), Taking(Set(shield, 10), 15)],
            Kills,
            0.01,
            catalogue
        );
        Assert.Equal([Id("RuneSword")], answer.Gains.Select(g => g.Item));
        var safer = Assert.Single(answer.Safer);
        Assert.Equal(Id("RuneShield"), safer.Item);
        Assert.Equal(25, safer.TakenBefore!.Point, 3);
        Assert.Equal(15, safer.Taken!.Point, 3);
        Assert.Equal(0, answer.NoGain);
    }
}
