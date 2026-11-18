using System.Numerics;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class DominanceTests
{
    private static readonly Goal Kills = new(Metrics.Kills);

    private static readonly string[] AllButArmour = ["Weapon", "Ammo", "Cape", "Amulet", "Ring1", "Ring2", "Gloves"];

    private static SearchRequest Request(string metric = Metrics.Kills, params string[] locks) =>
        SearchRequestTests.Request(metric: metric) with
        {
            Locks = locks.Select(Id).ToHashSet(),
        };

    private static Measure Measured(GearCatalogue? catalogue = null, Goal? goal = null) =>
        new(catalogue ?? Catalogue(), goal ?? Kills);

    private static List<string> Pool(SearchPlan plan, string slot) =>
        [.. plan.Branches[0].Units.Single(u => u.Slots.Contains(Id(slot))).Pool.Select(Ids.Brief)];

    [Fact]
    public void AnItemBetterInEverythingCountedBeatsAnother()
    {
        var measure = Measured();
        Assert.True(measure.Beats(Id("SteelHelmet"), Id("IronHelmet")));
        Assert.False(measure.Beats(Id("IronHelmet"), Id("SteelHelmet")));
        // Each is ahead in one stat: neither beats the other.
        Assert.False(measure.Beats(Id("SteelHelmet"), Id("MageHat")));
        Assert.False(measure.Beats(Id("MageHat"), Id("SteelHelmet")));
        Assert.True(measure.Beats(Id("IronHelmet"), null));
        Assert.False(measure.Beats(null, Id("IronHelmet")));
    }

    [Fact]
    public void ItemsOfDifferentKindsNeverCompare()
    {
        var measure = Measured();
        // An effect, a set, and a part the search doesn't know: each is a kind of its own.
        Assert.False(measure.Beats(Id("ThornCape"), Id("PlainCape")));
        Assert.False(measure.Beats(Id("PlainCape"), Id("ThornCape")));
        Assert.False(measure.Beats(Id("SteelHelmet"), Id("GuardHelmet")));
        Assert.False(measure.Beats(Id("AmuletOfAccuracy"), Id("OddAmulet")));
        Assert.False(measure.Beats(Id("OddAmulet"), null));
        Assert.False(measure.Comparable(Id("OddAmulet")));
    }

    [Fact]
    public void ABeatenItemGoes()
    {
        var plan = Planner.Plan(Catalogue(), Request(), comparing: true);
        Assert.Equal(["SteelHelmet", "MageHat", "GuardHelmet"], Pool(plan, "Helmet"));
        Assert.Equal(["SteelShield"], Pool(plan, "Offhand"));
        Assert.False(plan.Branches[0].Units.Single(u => u.Slots.Contains(Id("Helmet"))).AllowEmpty);
        Assert.True(plan.RuledOut[SearchPlan.Beaten] > 0);
    }

    [Fact]
    public void ARingGoesOnlyWhenBetterOnesFillBothSlots()
    {
        // Two Gold Rings beat the Iron Ring in both slots; the Silver Ring is ahead in another stat.
        Assert.Equal(["GoldRing", "SilverRing"], Pool(Planner.Plan(Catalogue(), Request(), comparing: true), "Ring1"));
        var oneGold = Catalogue(i => i.Id == Id("GoldRing") ? i with { Owned = 1 } : i);
        Assert.Equal(
            ["GoldRing", "SilverRing", "IronRing"],
            Pool(Planner.Plan(oneGold, Request(), comparing: true), "Ring1")
        );
    }

    [Fact]
    public void AnOpaqueItemStays()
    {
        var plan = Planner.Plan(Catalogue(), Request(), comparing: true);
        Assert.Contains("OddAmulet", Pool(plan, "Amulet"));
        Assert.Contains(plan.Candidates, c => c.Changes.Any(x => x.Value == Id("OddAmulet")));
    }

    [Fact]
    public void AnItemWithANeutralModifierStays()
    {
        var neutral = Catalogue(i =>
            i.Id == Id("AmuletOfAccuracy")
                ? i with
                {
                    Bonuses = [new("AccuracyPercent", null, -50, Direction.Neutral, PartGroup.Fight)],
                }
                : i
        );
        Assert.False(Measured(neutral).Comparable(Id("AmuletOfAccuracy")));
        Assert.Contains("AmuletOfAccuracy", Pool(Planner.Plan(neutral, Request(), comparing: true), "Amulet"));
        // Read as bad, the same modifier loses to wearing nothing.
        var bad = Catalogue(i =>
            i.Id == Id("AmuletOfAccuracy")
                ? i with
                {
                    Bonuses = [new("AccuracyPercent", null, 50, Direction.Bad, PartGroup.Fight)],
                }
                : i
        );
        Assert.DoesNotContain("AmuletOfAccuracy", Pool(Planner.Plan(bad, Request(), comparing: true), "Amulet"));
    }

    [Fact]
    public void ADefenceStatIsIgnoredWhenTheTargetCannotAttack()
    {
        var plan = Planner.Plan(Catalogue(targetAttacks: false), Request(), comparing: true);
        // Every plain helmet is then worth nothing, and the Mage Hat's magic beats them all. The shields all equal an
        // empty hand: the worn one stays.
        Assert.Equal(["MageHat", "GuardHelmet"], Pool(plan, "Helmet"));
        Assert.Equal(["StoneShield"], Pool(plan, "Offhand"));
        var withHat = plan.Candidates.First(c => c.Changes.Any(x => x.Value == Id("MageHat")));
        Assert.DoesNotContain(withHat.Changes, x => x.Subject == Id("Offhand"));
    }

    [Fact]
    public void ARewardsModifierCountsOnlyForWhatAKillGives()
    {
        Assert.DoesNotContain(
            "AmuletOfLearning",
            Pool(Planner.Plan(Catalogue(), Request(), comparing: true), "Amulet")
        );
        Assert.Contains(
            "AmuletOfLearning",
            Pool(Planner.Plan(Catalogue(), Request(Metrics.MainXp), comparing: true), "Amulet")
        );
    }

    [Fact]
    public void EqualItemsAreKeptAsOneAndNamed()
    {
        var twins = Catalogue(i =>
            i.Id == Id("IronHelmet") ? i with { Stats = new Dictionary<string, double> { [Defence] = 9 } } : i
        );
        var plan = Planner.Plan(twins, Request(Metrics.Kills, AllButArmour), comparing: true);
        Assert.Contains("IronHelmet", Pool(plan, "Helmet"));
        Assert.DoesNotContain("SteelHelmet", Pool(plan, "Helmet"));
        var withIron = plan.Candidates.First(c => c.Changes.Any(x => x.Value == Id("IronHelmet")));
        Assert.Contains(withIron.Same, same => same.Any(x => x.Value == Id("SteelHelmet")));
    }

    [Fact]
    public void WithComparingOffNothingIsRuledOut()
    {
        var plan = Planner.Plan(Catalogue(), Request(Metrics.Kills, AllButArmour), comparing: false);
        Assert.Empty(plan.RuledOut);
        Assert.Equal(plan.Possible, new BigInteger(plan.Candidates.Count));
        Assert.Contains("IronHelmet", Pool(plan, "Helmet"));
        // A metric nobody cleared is never compared.
        Assert.False(Goal.AllowsComparing("made-up-metric"));
    }

    [Fact]
    public void TheCountsAddUp()
    {
        var plan = Planner.Plan(Catalogue(), Request(Metrics.Kills, AllButArmour), comparing: true);
        var ruledOut = plan.RuledOut[SearchPlan.Beaten] + plan.RuledOut[SearchPlan.Totals];
        // Current gear is a candidate whether or not a rule would keep it.
        var listed = plan.Candidates.Count - 1;
        Assert.InRange((int)(plan.Possible - ruledOut) - listed, 0, 1);
        Assert.True(plan.Candidates.Count < 100);
    }

    [Fact]
    public void TheWholeSetBonusIsNotRuledOutByBetterPlainArmour()
    {
        var plan = Planner.Plan(Catalogue(), Request(Metrics.Kills, AllButArmour), comparing: true);
        string[] pieces = [Id("GuardHelmet"), Id("GuardBody"), Id("GuardLegs"), Id("GuardBoots")];
        Assert.Contains(plan.Candidates, c => pieces.All(p => c.Changes.Any(x => x.Value == p)));
    }

    [Fact]
    public void EverySetRuledOutIsBeatenOnTotalsByOneLeft()
    {
        var catalogue = Catalogue();
        var request = Request(Metrics.Kills, AllButArmour);
        var measure = Measured(catalogue);
        var all = Planner.Plan(catalogue, request, comparing: false).Candidates;
        var left = Planner.Plan(catalogue, request, comparing: true).Candidates;
        var kept = left.Select(c => (c, Total: Total(catalogue, measure, c))).ToList();
        Assert.True(left.Count < all.Count / 10);
        foreach (var candidate in all)
        {
            var (total, with, stance) = Total(catalogue, measure, candidate);
            Assert.True(
                kept.Any(k => k.Total.Stance == stance && k.Total.With == with && Measure.AtLeast(k.Total.Sum, total)),
                $"{candidate.Changes.ShortForm} is beaten by no set left"
            );
        }
    }

    // What a candidate wears in the slots a search may change, summed on the scale, with its sets and special items.
    private static (double[] Sum, string With, string Stance) Total(
        GearCatalogue catalogue,
        Measure measure,
        Candidate candidate
    )
    {
        var worn = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var slot in catalogue.Slots)
            worn[slot.Id] = catalogue.Current.GetValueOrDefault(slot.Id);
        var stance = catalogue.Stance;
        foreach (var change in candidate.Changes)
        {
            if (change.Kind == ChangeKind.Stance)
                stance = change.Value;
            else
                worn[change.Subject!] = change.Item;
        }
        var sum = new double[measure.Dimensions];
        var with = new List<string>();
        foreach (var (slot, item) in worn)
        {
            if (AllButArmour.Contains(Ids.Brief(slot)))
                continue;
            var vector = measure.Vector(item);
            for (var d = 0; d < sum.Length; d++)
                sum[d] += vector[d];
            if (measure.Token(item) is { Length: > 0 } token)
                with.Add(token);
        }
        return (sum, string.Join(' ', with.Order(StringComparer.Ordinal)), stance);
    }
}
