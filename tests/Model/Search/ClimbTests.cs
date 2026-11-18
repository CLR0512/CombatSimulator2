using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ClimbTests
{
    private static (GearCatalogue Catalogue, Climb Climb) Start(bool comparing = true, params string[] locks)
    {
        var catalogue = Catalogue();
        var request = SearchRequestTests.Request() with { Locks = locks.Select(Id).ToHashSet(), SlotAtATime = true };
        return (catalogue, new Climb(catalogue, Planner.Plan(catalogue, request, comparing).Branches));
    }

    // A scripted fight: a set is worth the strength and attack it adds, so the winner of a step is known.
    private static double Worth(GearCatalogue catalogue, ChangeList set) =>
        set.Where(c => c.Kind == ChangeKind.Equip && c.Item != null)
            .Sum(c =>
                catalogue.Item(c.Item!)!.Stats.GetValueOrDefault(Strength)
                + catalogue.Item(c.Item!)!.Stats.GetValueOrDefault(Attack)
            );

    private static ChangeList Run(GearCatalogue catalogue, Climb climb, List<int>? sizes = null)
    {
        while (climb.Next() is { } sets)
        {
            sizes?.Add(sets.Count);
            // Current gear's own items count too, so a swap is judged by what it adds over them; the held set wins a
            // draw, as a set no better than it is no reason to change.
            double Score(ChangeList set) => Worth(catalogue, set) - Removed(catalogue, set);
            var best = sets.MaxBy(Score)!;
            climb.Won(Score(best) > Score(climb.Held) ? best : climb.Held);
        }
        return climb.Held;
    }

    private static double Removed(GearCatalogue catalogue, ChangeList set) =>
        set.Where(c => c.Kind == ChangeKind.Equip)
            .Select(c => catalogue.Current.GetValueOrDefault(c.Subject!))
            .Where(item => item != null)
            .Sum(item =>
                catalogue.Item(item!)!.Stats.GetValueOrDefault(Strength)
                + catalogue.Item(item!)!.Stats.GetValueOrDefault(Attack)
            );

    [Fact]
    public void TheFirstStepIsTheWeaponAndStance()
    {
        var (_, climb) = Start();
        var sets = climb.Next()!;
        // Thirteen branches, one of them current gear's own.
        Assert.Equal(13, sets.Count);
        Assert.Contains(ChangeList.Empty, sets);
        Assert.Contains(sets, s => s.ShortForm == "stance=Stab");
        Assert.Contains(sets, s => s.ShortForm == "equip=Weapon:Iron2HSword stance=Block");
        Assert.All(sets, s => Assert.DoesNotContain(s, c => c.Kind == ChangeKind.Equip && c.Subject != Id("Weapon")));
    }

    [Fact]
    public void EachStepHoldsTheRestAndKeepsItsWinner()
    {
        var (catalogue, climb) = Start();
        var sizes = new List<int>();
        var found = Run(catalogue, climb, sizes);
        Assert.True(climb.Done);
        Assert.Null(climb.Next());
        // The two-hander adds the most; the gloves, rings and arrows follow, each in its own step.
        Assert.Contains(found, c => c.Value == Id("Iron2HSword"));
        Assert.Contains(found, c => c.Value == Id("IronGloves"));
        Assert.Equal(2, found.Count(c => c.Value == Id("GoldRing")));
        Assert.Contains(found, c => c.Value == Id("SteelArrows"));
        Assert.DoesNotContain(found, c => c.Subject == Id("Offhand"));
        // Far fewer sets fought than the millions possible.
        Assert.True(sizes.Sum() < 200);
    }

    [Fact]
    public void APassThatChangesNothingEndsTheClimb()
    {
        var (catalogue, climb) = Start();
        Run(catalogue, climb);
        Assert.Equal(2, climb.Passes);
    }

    [Fact]
    public void NoStepIsRacedTwiceFromTheSameHeldSet()
    {
        var (catalogue, climb) = Start();
        var raced = new List<(int Step, ChangeList Held)>();
        double Score(ChangeList set) => Worth(catalogue, set) - Removed(catalogue, set);
        while (climb.Next() is { } sets)
        {
            raced.Add((climb.Step, climb.Held));
            var best = sets.MaxBy(Score)!;
            climb.Won(Score(best) > Score(climb.Held) ? best : climb.Held);
        }
        Assert.Equal(raced.Count, raced.Distinct().Count());
        // The second pass, which changes nothing, races only the steps before the first pass's last change.
        Assert.Equal(2, climb.Passes);
        Assert.True(raced.Count < 2 * climb.StepsInPass);
    }

    [Fact]
    public void ThreePassesAtMost()
    {
        var (_, climb) = Start();
        var flip = 0;
        while (climb.Next() is { } sets)
            // A winner that never settles: a different set every step.
            climb.Won(
                sets[flip++ % sets.Count] == climb.Held ? sets[flip % sets.Count] : sets[(flip - 1) % sets.Count]
            );
        Assert.Equal(Climb.MostPasses, climb.Passes);
        Assert.True(climb.Done);
    }

    [Fact]
    public void KeepingTheHeldSetEveryStepIsCurrentGear()
    {
        var (_, climb) = Start();
        while (climb.Next() is { } sets)
        {
            Assert.Contains(climb.Held, sets);
            climb.Won(climb.Held);
        }
        Assert.Equal(ChangeList.Empty, climb.Held);
        Assert.Equal(1, climb.Passes);
    }

    [Fact]
    public void TheSameSetReadAgainIsTheSameSet()
    {
        var (catalogue, climb) = Start();
        // Winners handed back as lists read anew, as a search does that keeps its sets by their text.
        while (climb.Next() is { } sets)
        {
            double Score(ChangeList set) => Worth(catalogue, set) - Removed(catalogue, set);
            var best = sets.MaxBy(Score)!;
            var winner = Score(best) > Score(climb.Held) ? best : climb.Held;
            climb.Won(ChangeList.Parse(winner.ShortForm));
        }
        Assert.Equal(2, climb.Passes);
    }

    [Fact]
    public void AWinnerNotOfferedIsRefused()
    {
        var (_, climb) = Start();
        climb.Next();
        Assert.Throws<ArgumentException>(() => climb.Won(ChangeList.Parse("equip=Helmet:DragonHelmet")));
    }

    [Fact]
    public void AStepWithOnlyTheHeldSetIsPassedOver()
    {
        var all = Slots.Select(s => Ids.Brief(s.Id)).Where(s => s != "Helmet").ToArray();
        var (_, climb) = Start(comparing: false, all);
        // The locked weapon fixes its stance: only the helmet's unit has choices.
        var steps = new List<IReadOnlyList<ChangeList>>();
        while (climb.Next() is { } sets)
        {
            steps.Add(sets);
            climb.Won(climb.Held);
        }
        var step = Assert.Single(steps);
        Assert.All(step, s => Assert.All(s, c => Assert.Equal(Id("Helmet"), c.Subject)));
    }

    [Fact]
    public void MovingToATwoSlotWeaponDropsWhatItsOtherSlotHeld()
    {
        var (catalogue, climb) = Start();
        var sets = climb.Next()!;
        climb.Won(sets.First(s => s.ShortForm == "equip=Weapon:Iron2HSword stance=Slash"));
        // While it is held no step offers a shield: the weapon's branch has no offhand to choose.
        while (climb.Next() is { } next)
        {
            Assert.All(
                next.Where(s => s.Any(c => c.Value == Id("Iron2HSword"))),
                s => Assert.DoesNotContain(s, c => c.Subject == Id("Offhand"))
            );
            climb.Won(climb.Held);
        }
        Assert.Equal("equip=Weapon:Iron2HSword stance=Slash", climb.Held.ShortForm);
        Assert.NotNull(catalogue.Item(Id("Iron2HSword")));
    }

    [Fact]
    public void EachPassEndsWithTheSetPiecesWornTogetherAtEachCount()
    {
        var (_, climb) = Start();
        string[] guard = ["GuardHelmet", "GuardBody", "GuardLegs", "GuardBoots"];
        IReadOnlyList<ChangeList>? step = null;
        while (climb.Next() is { } sets)
        {
            if (sets.Any(set => guard.All(piece => set.Any(c => c.Value == Id(piece)))))
                step ??= sets;
            climb.Won(climb.Held);
        }
        // The guard set gives something at 2 and at 4 pieces: each pair of the four, and all four.
        var counts = step!
            .Select(set => guard.Count(piece => set.Any(c => c.Value == Id(piece))))
            .Where(n => n > 0)
            .Order()
            .ToList();
        Assert.Equal([2, 2, 2, 2, 2, 2, 4], counts);
    }

    [Fact]
    public void ASecondClimbStartsFromTheWeaponStepsRunnerUp()
    {
        var (_, climb) = Start();
        var sets = climb.Next()!;
        var runnerUp = sets.First(s => s.ShortForm == "equip=Weapon:IronBattleaxe stance=Slash");
        climb.Won(ChangeList.Empty, runnerUp);
        Assert.Null(climb.Restart());
        while (climb.Next() is not null)
            climb.Won(climb.Held);
        var again = climb.Restart()!;
        Assert.True(again.Restarted);
        Assert.Equal(runnerUp, again.Held);
        // It keeps the runner-up's weapon: no step races weapons or stances.
        while (again.Next() is { } next)
        {
            Assert.DoesNotContain(next, s => s.ShortForm == "stance=Stab");
            again.Won(again.Held);
        }
        Assert.Null(again.Restart());
    }

    [Fact]
    public void GearWithoutAWeaponClimbsAndNeverRestartsFromIt()
    {
        var catalogue = Catalogue(i => i.Name == "StoneSword" ? i with { Worn = null } : i);
        var request = SearchRequestTests.Request() with { SlotAtATime = true };
        var climb = new Climb(catalogue, Planner.Plan(catalogue, request).Branches);
        var sets = climb.Next()!;
        Assert.Contains(climb.Held, sets);
        Assert.Null(climb.WeaponOf(climb.Held));
        Assert.Equal("", climb.StyleOf(climb.Held));
        // Current gear as runner-up: there is no weapon to climb from.
        var sword = sets.First(s => climb.WeaponOf(s) == Id("SteelSword"));
        climb.Won(sword, climb.Held);
        Run(catalogue, climb);
        Assert.Null(climb.Restart());
    }

    // Prayers A to F, each worth its place in the alphabet backwards (A the most), and a none.
    private static ChoiceSteps Kinds(int slots = 2, bool food = false)
    {
        ChoiceOption Prayer(string id) => new(ChoiceKind.Prayers, [id], Choices.PrayerChanges([], [id]));
        var prayers = new Choice(
            ChoiceKind.Prayers,
            null,
            new ChoiceOption(ChoiceKind.Prayers, [], ChangeList.Empty),
            [new ChoiceOption(ChoiceKind.Prayers, [], ChangeList.Empty), .. "ABCDEF".Select(c => Prayer(c.ToString()))],
            slots
        );
        var trout = new ChoiceOption(ChoiceKind.Food, [Id("Trout")], ChangeList.Parse("food=Trout"));
        var foods = new Choice(ChoiceKind.Food, null, new ChoiceOption(ChoiceKind.Food, [], ChangeList.Empty), [trout]);
        return new ChoiceSteps(food ? [prayers, foods] : [prayers], IncludeUnowned: false);
    }

    private static double PrayerWorth(ChangeList set) =>
        set.Where(c => c.Kind == ChangeKind.Prayer && c.TurnsOn).Sum(c => 'G' - Ids.Brief(c.Subject!)[0]);

    // As Run, with prayers adding their worth and each step's ranking given to the climb.
    private static ChangeList RunWithKinds(GearCatalogue catalogue, Climb climb, List<IReadOnlyList<ChangeList>> steps)
    {
        while (climb.Next() is { } sets)
        {
            steps.Add(sets);
            double Score(ChangeList set) => Worth(catalogue, set) - Removed(catalogue, set) + PrayerWorth(set);
            var ranking = sets.OrderByDescending(Score).ToList();
            var best = ranking[0];
            climb.Won(Score(best) > Score(climb.Held) ? best : climb.Held, ranking: ranking);
        }
        return climb.Held;
    }

    [Fact]
    public void AClimbWithoutKindsHasNoChoiceSteps()
    {
        var (catalogue, plain) = Start();
        var withNone = new Climb(
            catalogue,
            Planner.Plan(catalogue, SearchRequestTests.Request() with { SlotAtATime = true }).Branches,
            new ChoiceSteps([], false)
        );
        Assert.Equal(plain.StepsInPass, withNone.StepsInPass);
        var a = new List<IReadOnlyList<ChangeList>>();
        var b = new List<IReadOnlyList<ChangeList>>();
        RunWithKinds(catalogue, plain, a);
        RunWithKinds(catalogue, withNone, b);
        Assert.Equal(a.Select(s => string.Join('|', s)), b.Select(s => string.Join('|', s)));
    }

    [Fact]
    public void PrayersComeAfterTheGearOneAtATimeThenInPairsOfTheBestFour()
    {
        var (catalogue, _) = Start();
        var branches = Planner.Plan(catalogue, SearchRequestTests.Request() with { SlotAtATime = true }).Branches;
        var climb = new Climb(catalogue, branches, Kinds());
        var steps = new List<IReadOnlyList<ChangeList>>();
        var found = RunWithKinds(catalogue, climb, steps);
        var singles = steps.First(s => s.Any(set => set.Any(c => c.Kind == ChangeKind.Prayer)));
        // None (the setup's own) and the six singles.
        Assert.Equal(7, singles.Count);
        Assert.All(singles, set => Assert.True(set.Count(c => c.Kind == ChangeKind.Prayer) <= 1));
        var pairs = steps[steps.IndexOf(singles) + 1];
        Assert.Equal(6 + 1, pairs.Count);
        Assert.DoesNotContain(pairs, set => set.Any(c => c.Subject is { } p && Ids.Brief(p) is "E" or "F"));
        Assert.Equal(["A", "B"], found.Where(c => c.Kind == ChangeKind.Prayer).Select(c => Ids.Brief(c.Subject!)));
        // The gear the plain climb finds is found with the prayers too.
        var (_, plain) = Start();
        Assert.Equal(
            Run(catalogue, plain).ShortForm,
            ChangeList.Of(found.Where(c => c.Kind != ChangeKind.Prayer)).ShortForm
        );
    }

    [Fact]
    public void WithOneSlotThereIsNoPairsStep()
    {
        var (catalogue, plain) = Start();
        var branches = Planner.Plan(catalogue, SearchRequestTests.Request() with { SlotAtATime = true }).Branches;
        var climb = new Climb(catalogue, branches, Kinds(slots: 1, food: true));
        Assert.Equal(plain.StepsInPass + 2, climb.StepsInPass);
        var found = RunWithKinds(catalogue, climb, []);
        Assert.Equal(["A"], found.Where(c => c.Kind == ChangeKind.Prayer).Select(c => Ids.Brief(c.Subject!)));
        // Trout adds nothing here, so the setup's own food is kept.
        Assert.DoesNotContain(found, c => c.Kind == ChangeKind.Food);
    }

    [Fact]
    public void AClimbFromAFoundSetStartsAtTheChoices()
    {
        var (catalogue, plain) = Start();
        var branches = Planner.Plan(catalogue, SearchRequestTests.Request() with { SlotAtATime = true }).Branches;
        var gear = Run(catalogue, plain);
        var climb = Climb.FromSet(catalogue, branches, gear, Kinds());
        var first = climb.Next()!;
        Assert.All(
            first,
            set => Assert.Equal(gear.ShortForm, ChangeList.Of(set.Where(c => c.Kind != ChangeKind.Prayer)).ShortForm)
        );
        Assert.Equal(7, first.Count);
    }

    [Fact]
    public void SetsWithUnusableChoicesSayWhichToIgnoreRequirementsFor()
    {
        var (catalogue, _) = Start();
        var branches = Planner.Plan(catalogue, SearchRequestTests.Request() with { SlotAtATime = true }).Branches;
        var locked = new ChoiceOption(ChoiceKind.Food, [Id("Shark")], ChangeList.Parse("food=Shark")) { Owned = false };
        var foods = new Choice(
            ChoiceKind.Food,
            null,
            new ChoiceOption(ChoiceKind.Food, [], ChangeList.Empty),
            [locked]
        );
        var off = Climb.FromSet(catalogue, branches, ChangeList.Empty, new ChoiceSteps([foods], IncludeUnowned: false));
        Assert.Null(off.Next());
        var on = Climb.FromSet(catalogue, branches, ChangeList.Empty, new ChoiceSteps([foods], IncludeUnowned: true));
        var sets = on.Next()!;
        var shark = sets.Single(s => s.Any(c => c.Kind == ChangeKind.Food));
        Assert.Equal("food=Shark", on.IgnoringOf(shark).ShortForm);
        Assert.True(on.IgnoringOf(ChangeList.Empty).IsEmpty);
    }
}
