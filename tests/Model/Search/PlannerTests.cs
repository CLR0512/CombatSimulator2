using System.Numerics;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PlannerTests
{
    private static SearchRequest Request(params string[] locks) =>
        SearchRequestTests.Request() with
        {
            Locks = locks.Select(Id).ToHashSet(),
        };

    // Everything locked but the armour and the shield: small enough to list in full.
    private static readonly string[] AllButArmour = ["Weapon", "Ammo", "Cape", "Amulet", "Ring1", "Ring2", "Gloves"];

    private static IEnumerable<string> Equipped(SearchPlan plan) =>
        plan
            .Candidates.SelectMany(c => c.Changes)
            .Where(c => c.Kind == ChangeKind.Equip)
            .Select(c => c.Value)
            .Distinct();

    [Fact]
    public void EverySetTheCharacterCouldWearIsCounted()
    {
        // Eleven one-handed branches (three swords in three stances, an axe in two) and two for the two-hander,
        // which takes the offhand: 4 shields × 3 ammo × 6 helmets × 5 bodies × 4 legs × 4 boots × 2 gloves × 3 capes
        // × 4 amulets × 8 ring pairs, each with nothing as a choice.
        var plan = Planner.Plan(Catalogue(), Request(), comparing: false);
        Assert.Equal(new BigInteger(11 * 1_105_920 + 2 * 276_480), plan.Possible);
        Assert.Equal(13, plan.Branches.Count);
        Assert.All(plan.Branches, b => Assert.Equal("Melee", b.Style));
        Assert.True(plan.Climb);
        Assert.Empty(plan.RuledOut);
    }

    [Fact]
    public void CurrentGearIsAlwaysTheFirstCandidate()
    {
        foreach (var comparing in new[] { true, false })
        {
            var plan = Planner.Plan(Catalogue(), Request(AllButArmour), comparing);
            Assert.True(plan.Candidates[0].IsCurrent);
            Assert.Equal("Melee", plan.Candidates[0].Style);
            Assert.Single(plan.Candidates, c => c.IsCurrent);
            Assert.Equal(Enumerable.Range(0, plan.Candidates.Count), plan.Candidates.Select(c => c.Index));
        }
    }

    [Fact]
    public void WithoutComparingEverySetIsACandidate()
    {
        var plan = Planner.Plan(Catalogue(), Request(AllButArmour), comparing: false);
        // The locked Stone Sword in the stance fought in now.
        Assert.Equal(new BigInteger(4 * 6 * 5 * 4 * 4), plan.Possible);
        Assert.False(plan.Climb);
        Assert.Equal(1920, plan.Candidates.Count);
        Assert.Equal(plan.Candidates.Count, plan.Candidates.Select(c => c.Changes.ShortForm).Distinct().Count());
        Assert.DoesNotContain(plan.Candidates, c => c.Changes.Any(x => x.Kind == ChangeKind.Stance));
        // A weapon left free fights in each of its stances.
        var free = Planner.Plan(Catalogue(), Request([.. AllButArmour.Where(s => s != "Weapon")]), comparing: false);
        Assert.Equal(3, free.Branches.Count(b => b.Weapon == Id("StoneSword")));
    }

    [Fact]
    public void OnlyOwnedWearableItemsAreOffered()
    {
        var plan = Planner.Plan(Catalogue(), Request(AllButArmour), comparing: false);
        var equipped = Equipped(plan).ToList();
        Assert.Contains(Id("SteelHelmet"), equipped);
        Assert.Contains(Change.None, equipped);
        Assert.DoesNotContain(Id("DragonHelmet"), equipped);
        Assert.DoesNotContain(Id("RuneShield"), equipped);
        Assert.DoesNotContain(Id("MithrilPlatebody"), equipped);
        Assert.Contains(plan.LeftOut, l => l.What == "MithrilPlatebody" && l.Why.English() == "Defence level 50");
    }

    [Fact]
    public void ATwoSlotWeaponLeavesItsOtherSlotToTheGame()
    {
        var plan = Planner.Plan(
            Catalogue(),
            Request("Ammo", "Cape", "Amulet", "Ring1", "Ring2", "Gloves", "Helmet", "Platebody", "Platelegs", "Boots"),
            comparing: false
        );
        var twoHanded = plan.Candidates.Where(c => c.Changes.Any(x => x.Value == Id("Iron2HSword"))).ToList();
        Assert.Equal(2, twoHanded.Count);
        Assert.All(twoHanded, c => Assert.DoesNotContain(c.Changes, x => x.Subject == Id("Offhand")));
        Assert.All(twoHanded, c => Assert.Contains(c.Changes, x => x.Kind == ChangeKind.Stance));
        // A new weapon names its stance even when it is the one fought in now.
        Assert.Contains(plan.Candidates, c => c.Changes.ShortForm == "equip=Weapon:IronSword stance=Slash");
    }

    [Fact]
    public void TwoRingsOfOneItemNeedTwoOwned()
    {
        var rings = Planner
            .Plan(Catalogue(), Request(), comparing: false)
            .Branches[0]
            .Units.Single(u => u.Slots.Count == 2);
        var pairs = rings
            .Choices()
            .Select(c => string.Join('+', c.Select(i => i == null ? "-" : Ids.Brief(i))))
            .ToList();
        Assert.Equal(8, pairs.Count);
        Assert.Contains("GoldRing+GoldRing", pairs);
        Assert.DoesNotContain("SilverRing+SilverRing", pairs);
        Assert.Contains("GoldRing+SilverRing", pairs);
        Assert.DoesNotContain("SilverRing+GoldRing", pairs);
        Assert.Contains("-+-", pairs);
    }

    [Fact]
    public void AWeaponThatFiresHasItsAmmoAndNothingElseThere()
    {
        var plan = Planner.Plan(Catalogue(), Request() with { AllStyles = true }, comparing: false);
        var bow = plan.Branches.First(b => b.Weapon == Id("IronBow"));
        Assert.Equal("Ranged", bow.Style);
        Assert.Null(bow.Fixed[Id("Offhand")]);
        var ammo = bow.Units.Single(u => u.Slots.Contains(Id("Ammo")));
        Assert.Equal([Id("IronArrows"), Id("SteelArrows")], ammo.Pool);
        Assert.False(ammo.AllowEmpty);
        var noArrows = Planner.Plan(
            Catalogue(i => i.AmmoType == null ? i : i with { Owned = 0 }),
            Request() with
            {
                AllStyles = true,
            },
            comparing: false
        );
        Assert.DoesNotContain(noArrows.Branches, b => b.Weapon == Id("IronBow"));
        Assert.Contains(noArrows.LeftOut, l => l.What == "IronBow");
    }

    [Fact]
    public void AStyleTheSetupCannotFightInIsSkipped()
    {
        var catalogue = Catalogue() with
        {
            UnusableStyles = new Dictionary<string, Note> { ["Ranged"] = Note.Of("no ammo") },
        };
        var plan = Planner.Plan(catalogue, Request() with { AllStyles = true }, comparing: false);
        Assert.Equal(["Ranged"], plan.SkippedStyles.Select(s => s.Style));
        Assert.DoesNotContain(plan.Branches, b => b.Style == "Ranged");
    }

    [Fact]
    public void AWornItemNoLongerWearableStaysOnlyInCurrentGear()
    {
        var catalogue = Catalogue(i =>
            i.Id == Id("StonePlatebody") ? i with { Unmet = [Note.Text("Defence level 5")] } : i
        );
        var plan = Planner.Plan(catalogue, Request(AllButArmour), comparing: false);
        Assert.True(plan.Candidates[0].IsCurrent);
        Assert.All(plan.Candidates.Skip(1), c => Assert.Contains(c.Changes, x => x.Subject == Id("Platebody")));
        Assert.DoesNotContain(Id("StonePlatebody"), Equipped(plan));
    }

    [Fact]
    public void ALockedSlotStaysAsWorn()
    {
        var plan = Planner.Plan(Catalogue(), Request("Weapon", "Helmet"), comparing: true);
        Assert.NotEmpty(plan.Candidates.Skip(1));
        Assert.All(plan.Candidates, c => Assert.DoesNotContain(c.Changes, x => x.Subject == Id("Helmet")));
        Assert.All(plan.Candidates, c => Assert.DoesNotContain(c.Changes, x => x.Subject == Id("Weapon")));
        // The locked weapon fixes the branch, its stance included.
        Assert.Single(plan.Branches);
        Assert.Equal(Id("Slash"), plan.Branches[0].Stance);
        Assert.All(plan.Candidates, c => Assert.DoesNotContain(c.Changes, x => x.Kind == ChangeKind.Stance));
        Assert.All(plan.Branches, b => Assert.Equal(Id("StoneSword"), b.Weapon));
    }

    [Fact]
    public void ALockedOffhandRulesOutATwoSlotWeapon()
    {
        var plan = Planner.Plan(Catalogue(), Request("Offhand"), comparing: false);
        Assert.DoesNotContain(plan.Branches, b => b.Weapon == Id("Iron2HSword"));
        Assert.All(plan.Branches, b => Assert.Equal(Id("StoneShield"), b.Fixed[Id("Offhand")]));
    }

    [Fact]
    public void ALockedRingIsNotFreeForTheOtherSlot()
    {
        var catalogue = Catalogue(i => i.Id == Id("SilverRing") ? i with { Worn = Id("Ring1") } : i);
        var ring = Planner
            .Plan(catalogue, Request("Ring1"), comparing: false)
            .Branches[0]
            .Units.Single(u => u.Slots.Contains(Id("Ring2")));
        Assert.Equal([Id("Ring2")], ring.Slots);
        Assert.DoesNotContain(Id("SilverRing"), ring.Pool);
        Assert.Contains(Id("GoldRing"), ring.Pool);
    }

    [Fact]
    public void AnExcludedItemIsNowhere()
    {
        var request = Request(AllButArmour) with
        {
            Excluded = new HashSet<string> { Id("SteelHelmet"), Id("GuardBody") },
        };
        var plan = Planner.Plan(Catalogue(), request, comparing: false);
        Assert.DoesNotContain(Id("SteelHelmet"), Equipped(plan));
        Assert.DoesNotContain(Id("GuardBody"), Equipped(plan));
        Assert.Contains(Id("IronHelmet"), Equipped(plan));
    }

    [Fact]
    public void WithEverySlotLockedThereIsNothingToSearch()
    {
        var all = Slots.Select(s => Ids.Brief(s.Id)).ToArray();
        var plan = Planner.Plan(Catalogue(), Request(all), comparing: true);
        Assert.True(plan.NothingToSearch);
        Assert.Single(plan.Candidates);
        Assert.Equal(BigInteger.One, plan.Possible);
        Assert.False(Planner.Plan(Catalogue(), Request(AllButArmour), comparing: true).NothingToSearch);
    }

    [Fact]
    public void AShortSearchListsNoSets()
    {
        var plan = Planner.Plan(Catalogue(), Request(AllButArmour) with { SlotAtATime = true }, comparing: false);
        Assert.True(plan.Climb);
        Assert.Single(plan.Candidates);
        Assert.False(plan.NothingToSearch);
    }

    // Everything locked but the weapon, the Steel Sword owned but not wearable yet.
    private static readonly string[] AllButWeapon =
    [
        "Ammo",
        "Offhand",
        "Cape",
        "Amulet",
        "Ring1",
        "Ring2",
        "Gloves",
        "Helmet",
        "Platebody",
        "Platelegs",
        "Boots",
    ];

    private static GearCatalogue SwordLocked() =>
        Catalogue(i => i.Id == Id("SteelSword") ? i with { Unmet = [Note.Text("Attack Level 95")] } : i);

    [Fact]
    public void GearOwnedButNotWearableIsLeftOutByDefault()
    {
        var plan = Planner.Plan(SwordLocked(), Request(AllButWeapon), comparing: false);
        Assert.DoesNotContain(Id("SteelSword"), Equipped(plan));
        Assert.Contains(plan.LeftOut, l => l.What == "SteelSword" && l.Why.English() == "Attack Level 95");
    }

    [Fact]
    public void IncludingWhatTheCharacterDoesntHaveTriesGearItCantWearYet()
    {
        var plan = Planner.Plan(SwordLocked(), Request(AllButWeapon) with { IncludeUnowned = true }, comparing: false);
        // One candidate per stance the sword fights in, each with the sword's requirements ignored and nothing else.
        var swords = plan.Candidates.Where(c => c.Changes.Any(x => x.Item == Id("SteelSword"))).ToList();
        Assert.NotEmpty(swords);
        Assert.All(
            swords,
            c => Assert.Equal([new Change(ChangeKind.Equip, Id("Weapon"), Id("SteelSword"))], c.Ignoring)
        );
        Assert.All(plan.Candidates.Except(swords), c => Assert.Empty(c.Ignoring));
        Assert.DoesNotContain(plan.LeftOut, l => l.What == "SteelSword");
    }

    [Fact]
    public void ASetupIgnoringRequirementsDoesntLetLockedGearInByItself()
    {
        var request = Request(AllButWeapon);
        request.Setup.IgnoreRequirements = true;
        var plan = Planner.Plan(SwordLocked(), request, comparing: false);
        Assert.DoesNotContain(Id("SteelSword"), Equipped(plan));
        Assert.Contains(plan.LeftOut, l => l.What == "SteelSword");
    }

    [Fact]
    public void TheAnswerMarksGearTheCharacterCantWearYet()
    {
        var catalogue = SwordLocked();
        var candidate = new Candidate(1, ChangeList.Parse("equip=Weapon:SteelSword"), "Melee");
        var result = new StageResult(1, RankingTests.Finished("t", "t", 20), "kills");
        var set = Answers.Set(candidate, result, null, catalogue);
        var mark = Assert.Single(set.Slots).Mark;
        Assert.Equal(MarkKind.Locked, mark!.Kind);
        Assert.Equal("Attack Level 95", mark.Needs!.English());
        Assert.True(set.NeedsRequirementsIgnored);
    }
}
