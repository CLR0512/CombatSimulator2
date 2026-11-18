using System.Diagnostics;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// The search's steps on a catalogue at the size a full game's may reach: about 3,000 wearable items, every one
/// owned, a few dozen set bonuses and some items the search can't read. Each step must stay quick enough that a
/// player starting a search waits on fights, not on planning.
/// </summary>
public sealed class BigCatalogueTests(ITestOutputHelper output)
{
    private static readonly string[] Styles = ["Melee", "Ranged", "Magic"];
    private static readonly string[] Extra = ["rangedStrength", "magicDamage", "evasion", "ward"];

    // The game's slots beside the weapon, as its data has them (the second ring slot unlocks late), and how many
    // items each gets.
    private static readonly (string Type, int Slots, int Items)[] Layout =
    [
        ("Helmet", 1, 280),
        ("Cape", 1, 260),
        ("Amulet", 1, 260),
        ("Ammo", 1, 60),
        ("Torso", 1, 280),
        ("Offhand", 1, 240),
        ("Legs", 1, 280),
        ("Ring", 2, 260),
        ("Gloves", 1, 260),
        ("Boots", 1, 260),
    ];

    private const int Weapons = 560;

    /// <summary>A seeded catalogue: the same items on every run, so a slow step stays slow until it is fixed.</summary>
    internal static GearCatalogue Big(int seed = 2026, double owned = 1)
    {
        var random = new Random(seed);
        var slots = new List<GearSlot> { new(Id("Weapon"), "Weapon") };
        foreach (var (type, count, _) in Layout)
            for (var n = 1; n <= count; n++)
                slots.Add(new GearSlot(Id(count == 1 ? type : type + n), type));
        var items = new List<GearItem>();
        GearItem Make(string name, string type)
        {
            var opaque = random.NextDouble() < 0.04;
            return new(Id(name), name, type)
            {
                Owned = random.NextDouble() < owned ? 1 : 0,
                Stats = Stats(random),
                Bonuses = random.NextDouble() < 0.25 ? [Bonus(random)] : [],
                Opaque = opaque,
                OpaqueReason = opaque ? "effect kind Odd" : null,
            };
        }
        for (var w = 0; w < Weapons; w++)
        {
            var style = Styles[w % Styles.Length];
            var ranged = style == "Ranged";
            items.Add(
                Make($"Weapon{w}", "Weapon") with
                {
                    Weapon = new WeaponPart(
                        style + "Weapon",
                        style,
                        [Id(style + "A"), Id(style + "B"), Id(style + "C")],
                        AmmoType: ranged ? (w % 2 == 0 ? "Arrow" : "Bolt") : null
                    ),
                    Occupies = random.NextDouble() < 0.3 ? [Id("Offhand")] : [],
                }
            );
        }
        foreach (var (type, _, count) in Layout)
            for (var i = 0; i < count; i++)
            {
                var item = Make($"{type}{i}", type);
                if (type == "Ammo")
                    item = item with { AmmoType = i % 2 == 0 ? "Arrow" : "Bolt", Stackable = true, Owned = 100 };
                items.Add(item);
            }
        var bonuses = SetBonuses(random, items);
        items =
        [
            .. items.Select(i => i with { Sets = [.. bonuses.Where(b => b.Items.Contains(i.Id)).Select(b => b.Id)] }),
        ];
        // Worn: the first melee weapon and the first item of each other slot, ammo aside.
        var worn = new Dictionary<string, string>(StringComparer.Ordinal) { [Id("Weapon")] = Id("Weapon0") };
        foreach (var slot in slots.Skip(1).Where(s => s.Type != "Ammo"))
            worn[slot.Id] = items.First(i => i.SlotType == slot.Type && !worn.ContainsValue(i.Id)).Id;
        items =
        [
            .. items.Select(i =>
                worn.ContainsValue(i.Id) ? i with { Owned = 1, Worn = worn.First(w => w.Value == i.Id).Key } : i
            ),
        ];
        return new GearCatalogue
        {
            Slots = slots,
            Items = items,
            SetBonuses = bonuses,
            Current = worn,
            Stance = Id("MeleeA"),
            StatGroups = new Dictionary<string, PartGroup>
            {
                [Attack] = PartGroup.Fight,
                [Strength] = PartGroup.Fight,
                [Magic] = PartGroup.Fight,
                [Defence] = PartGroup.Defence,
                [Extra[0]] = PartGroup.Fight,
                [Extra[1]] = PartGroup.Fight,
                [Extra[2]] = PartGroup.Defence,
                [Extra[3]] = PartGroup.Defence,
            },
            Levels = new Dictionary<string, int> { [Id("Attack")] = 99 },
            Build = "big",
        };
    }

    // Three to five stats each, so few items beat each other in all of them.
    private static Dictionary<string, double> Stats(Random random)
    {
        string[] all = [Attack, Strength, Magic, Defence, .. Extra];
        return all.OrderBy(_ => random.Next())
            .Take(random.Next(3, 6))
            .ToDictionary(s => s, _ => (double)random.Next(1, 120));
    }

    private static PartModifier Bonus(Random random) =>
        random.Next(3) switch
        {
            0 => new("AccuracyPercent", null, random.Next(1, 15), Direction.Good, PartGroup.Fight),
            1 => new("DamageReduction", null, random.Next(1, 10), Direction.Good, PartGroup.Defence),
            _ => new("XpPercent", null, random.Next(1, 10), Direction.Good, PartGroup.Xp),
        };

    // Forty bonuses of two to six pieces in different slots; each count from two up gives something.
    private static List<GearSetBonus> SetBonuses(Random random, List<GearItem> items)
    {
        var bonuses = new List<GearSetBonus>();
        var types = Layout.Where(l => l.Type != "Ammo").Select(l => l.Type).ToList();
        for (var b = 0; b < 40; b++)
        {
            var size = random.Next(2, 7);
            var pieces = types
                .OrderBy(_ => random.Next())
                .Take(size)
                .Select(type => items.Where(i => i.SlotType == type && i.Sets.Count == 0).ElementAt(random.Next(50)).Id)
                .ToList();
            var steps = Enumerable
                .Range(2, size - 1)
                .ToDictionary(
                    n => n,
                    n =>
                        (IReadOnlyList<PartModifier>)
                            [new("StrengthPercent", null, 2 * n, Direction.Good, PartGroup.Fight)]
                );
            bonuses.Add(new GearSetBonus(Id($"Set{b}"), $"Set {b}", pieces, steps));
        }
        return bonuses;
    }

    private static SearchRequest Request(bool allStyles = true) =>
        SearchRequestTests.Request() with
        {
            AllStyles = allStyles,
        };

    private TimeSpan Timed(string what, Action step)
    {
        var watch = Stopwatch.StartNew();
        step();
        watch.Stop();
        output.WriteLine($"{what}: {watch.Elapsed.TotalMilliseconds:0} ms");
        return watch.Elapsed;
    }

    [Fact]
    public void TheCatalogueIsAsBigAsMeant()
    {
        var catalogue = Big();
        Assert.InRange(catalogue.Items.Count, 3_000, 3_200);
        Assert.Equal(40, catalogue.SetBonuses.Count);
        Assert.Contains(catalogue.Items, i => i.Opaque);
        Assert.Equal(catalogue.Slots.Count - 1, catalogue.Current.Count);
    }

    [Fact]
    public void CountingAUnitAgreesWithListingIt()
    {
        var units = new[]
        {
            new Unit(
                ["a", "b"],
                ["x", "y", "z"],
                new Dictionary<string, int>
                {
                    ["x"] = 2,
                    ["y"] = 1,
                    ["z"] = 1,
                },
                true
            ),
            new Unit(["a", "b", "c"], ["x", "y"], new Dictionary<string, int> { ["x"] = 1, ["y"] = 5 }, false),
            new Unit(["a"], ["x", "y"], new Dictionary<string, int> { ["x"] = 0, ["y"] = 1 }, true),
            new Unit(["a", "b"], [], new Dictionary<string, int>(), true),
        };
        foreach (var unit in units)
            Assert.Equal(unit.Choices().Count(), (int)unit.Count);
        foreach (var branch in Planner.Plan(Catalogue(), Request(), comparing: false).Branches)
        foreach (var unit in branch.Units)
            Assert.Equal(unit.Choices().Count(), (int)unit.Count);
    }

    [Fact]
    public void PlanningEveryStyleStaysQuick()
    {
        var catalogue = Big();
        SearchPlan plan = null!;
        var fighting = Timed("plan, fighting all", () => plan = Planner.Plan(catalogue, Request(), comparing: false));
        output.WriteLine($"{plan.Branches.Count} branches, {plan.Possible:N0} possible");
        var comparing = Timed("plan, comparing", () => plan = Planner.Plan(catalogue, Request()));
        output.WriteLine($"{plan.Branches.Count} branches, climb {plan.Climb}");
        Assert.True(plan.Climb);
        Assert.InRange(fighting, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.InRange(comparing, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ClimbingStaysQuick()
    {
        var catalogue = Big();
        var plan = Planner.Plan(catalogue, Request() with { SlotAtATime = true });
        var climb = new Climb(catalogue, plan.Branches);
        var steps = 0;
        var offered = 0;
        var sizes = new List<int>();
        var took = Timed(
            "climb",
            () =>
            {
                // A scripted fight: the set with the most strength wins each step.
                while (climb.Next() is { } sets)
                {
                    steps++;
                    offered += sets.Count;
                    sizes.Add(sets.Count);
                    climb.Won(sets.Append(climb.Held).MaxBy(s => Worth(catalogue, s))!);
                }
            }
        );
        output.WriteLine($"{steps} steps, {offered} sets offered: {string.Join(' ', sizes)}");
        Assert.True(climb.Done);
        Assert.InRange(took, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DroptimizerPlanningStaysQuick()
    {
        var catalogue = Big(owned: 0.5);
        DropPlan plan = null!;
        var took = Timed(
            "droptimizer",
            () => plan = Droptimizer.Plan(catalogue, SearchRequestTests.Request(SearchKind.Droptimizer))
        );
        output.WriteLine($"{plan.Unowned} unowned, {plan.RuledOut} ruled out, {plan.Candidates.Count} to fight");
        Assert.InRange(took, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    private static double Worth(GearCatalogue catalogue, ChangeList set) =>
        set.Where(c => c.Item != null).Sum(c => catalogue.Item(c.Item!)!.Stats.GetValueOrDefault(Strength));
}
