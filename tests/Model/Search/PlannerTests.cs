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

    private static SearchRequest Every(params string[] locks) => Request(locks) with { Pool = ItemPool.Every };

    private static GearCatalogue With(params GearItem[] items)
    {
        var catalogue = Catalogue();
        return catalogue with { Items = [.. catalogue.Items, .. items] };
    }

    [Fact]
    public void EveryItemOffersWhatIsntOwnedOrWearableYet()
    {
        var plan = Planner.Plan(Catalogue(), Every(AllButArmour), comparing: false);
        var equipped = Equipped(plan).ToList();
        Assert.Contains(Id("DragonHelmet"), equipped);
        Assert.Contains(Id("RuneShield"), equipped);
        Assert.Contains(Id("MithrilPlatebody"), equipped);
        Assert.DoesNotContain(plan.LeftOut, l => l.What == "MithrilPlatebody");
        var weapons = Planner.Plan(Catalogue(), Every(), comparing: false).Branches.Select(b => b.Weapon).ToList();
        Assert.Contains(Id("RuneSword"), weapons);
    }

    [Fact]
    public void EveryItemLeavesOutWhatItIsTold()
    {
        var request = Every(AllButArmour) with { Excluded = new HashSet<string> { Id("DragonHelmet") } };
        Assert.DoesNotContain(Id("DragonHelmet"), Equipped(Planner.Plan(Catalogue(), request, comparing: false)));
    }

    [Fact]
    public void OnlyItemsTheLevelsDontMeetAreFoughtWithRequirementsIgnored()
    {
        var plan = Planner.Plan(Catalogue(), Every(AllButArmour), comparing: false);
        foreach (var candidate in plan.Candidates)
        {
            var items = candidate.Changes.Where(c => c.Kind == ChangeKind.Equip).Select(c => c.Value).ToHashSet();
            var ignored = candidate.Ignoring.Select(c => c.Value).ToHashSet();
            Assert.Equal(items.Contains(Id("DragonHelmet")), ignored.Contains(Id("DragonHelmet")));
            Assert.Equal(items.Contains(Id("MithrilPlatebody")), ignored.Contains(Id("MithrilPlatebody")));
            Assert.DoesNotContain(Id("RuneShield"), ignored);
        }
        Assert.Contains(plan.Candidates, c => c.Ignoring.Any(x => x.Value == Id("DragonHelmet")));
    }

    [Fact]
    public void OneRingNotOwnedFillsBothRingSlots()
    {
        var ruby = new GearItem(Id("RubyRing"), "RubyRing", "Ring")
        {
            Stats = new Dictionary<string, double> { [Strength] = 5 },
        };
        var locks = AllButArmour.Where(s => s is not ("Ring1" or "Ring2")).ToArray();
        var units = Planner
            .Plan(With(ruby), Every(locks), comparing: false)
            .Branches.SelectMany(b => b.Units)
            .Where(u => u.Slots.Count == 2)
            .ToList();
        Assert.NotEmpty(units);
        foreach (var rings in units)
        {
            Assert.Equal(2, rings.Available[Id("RubyRing")]);
            // Owned once, it may still be had twice: another can be got.
            Assert.Equal(2, rings.Available[Id("SilverRing")]);
            Assert.Equal(2, rings.Available[Id("GoldRing")]);
            Assert.Contains(rings.Choices(), c => c.All(r => r == Id("RubyRing")));
        }
        // With what is owned, a ring had once fills one slot only.
        var owned = Planner
            .Plan(With(ruby), Request(locks), comparing: false)
            .Branches.SelectMany(b => b.Units)
            .First(u => u.Slots.Count == 2);
        Assert.Equal(1, owned.Available[Id("SilverRing")]);
        Assert.DoesNotContain(Id("RubyRing"), owned.Pool);
    }

    [Fact]
    public void AWeaponFiresAmmoNotOwned()
    {
        var catalogue = Catalogue(i => i.AmmoType != null ? i with { Owned = 0 } : i);
        var request = Request() with { AllStyles = true };
        var owned = Planner.Plan(catalogue, request, comparing: false);
        Assert.DoesNotContain(owned.Branches, b => b.Weapon == Id("IronBow"));
        Assert.Contains(owned.LeftOut, l => l.What == "IronBow");
        var every = Planner.Plan(catalogue, request with { Pool = ItemPool.Every }, comparing: false);
        var bow = every.Branches.First(b => b.Weapon == Id("IronBow"));
        var ammo = bow.Units.Single(u => u.Slots.Contains(Id("Ammo")));
        Assert.Equal([Id("IronArrows"), Id("SteelArrows")], ammo.Pool);
        Assert.False(ammo.AllowEmpty);
        Assert.DoesNotContain(every.LeftOut, l => l.What == "IronBow");
        // A weapon whose ammo the game has none of is left out, and says so without "owned".
        var none = Planner.Plan(
            Catalogue(i => i.AmmoType != null ? i with { AmmoType = "Bolt" } : i),
            request with
            {
                Pool = ItemPool.Every,
            },
            comparing: false
        );
        Assert.Contains(
            none.LeftOut,
            l => l.What == "IronBow" && l.Why.English() == "nothing in the game for it to fire"
        );
    }

    [Fact]
    public void EveryItemListsNoUnownedItemAsLeftOut()
    {
        var wide = new GearItem(Id("WideCape"), "WideCape", "Cape") { Occupies = [Id("Amulet")] };
        var plan = Planner.Plan(With(wide), Every(), comparing: false);
        Assert.DoesNotContain(plan.LeftOut, l => l.What == "WideCape");
        Assert.Contains(
            Planner.Plan(With(wide with { Owned = 1 }), Every(), comparing: false).LeftOut,
            l => l.What == "WideCape"
        );
    }

    // Every item's narrowings: Rune Shield from the shop (reachable), Dragon Helmet a monster's drop (not reachable,
    // needs Defence 60), Rune Sword an upgrade (can't be judged).
    private static GearCatalogue Sourced() =>
        Catalogue(i =>
            i.Id == Id("RuneShield") ? i with { SourceNames = ["Shop"], Reachable = true }
            : i.Id == Id("DragonHelmet") ? i with { SourceNames = ["Black Dragon"], Reachable = false }
            : i.Id == Id("RuneSword") ? i with { SourceNames = ["Upgrade"], Reachable = null }
            : i
        );

    private static IEnumerable<string> Offered(SearchPlan plan) =>
        Equipped(plan).Concat(plan.Branches.Select(b => b.Weapon ?? ""));

    [Fact]
    public void WearableOnlyLeavesOutWhatTheLevelsDontMeet()
    {
        var plan = Planner.Plan(Sourced(), Every() with { WearableOnly = true }, comparing: false);
        var offered = Planner.Plan(Sourced(), Every(AllButArmour) with { WearableOnly = true }, comparing: false);
        Assert.DoesNotContain(Id("DragonHelmet"), Equipped(offered));
        Assert.DoesNotContain(Id("MithrilPlatebody"), Equipped(offered));
        Assert.Contains(Id("RuneShield"), Equipped(offered));
        Assert.Contains(plan.Branches, b => b.Weapon == Id("RuneSword"));
    }

    [Fact]
    public void ReachedOnlyKeepsWhatIsOwnedOrCanBeGotNow()
    {
        var request = Every(AllButArmour) with { ReachedOnly = true };
        var plan = Planner.Plan(Sourced(), request, comparing: false);
        Assert.Contains(Id("RuneShield"), Equipped(plan));
        Assert.Contains(Id("MithrilPlatebody"), Equipped(plan));
        Assert.DoesNotContain(Id("DragonHelmet"), Equipped(plan));
        // One that can't be judged is left out and said.
        var weapons = Planner.Plan(Sourced(), Every() with { ReachedOnly = true }, comparing: false);
        Assert.DoesNotContain(weapons.Branches, b => b.Weapon == Id("RuneSword"));
        Assert.Contains(
            weapons.LeftOut,
            l => l.What == "RuneSword" && l.Why.English() == "can't tell whether it can be got"
        );
    }

    [Fact]
    public void LeavingOutASourceLeavesOutWhatOnlyItGives()
    {
        var shop = Planner.Plan(
            Sourced(),
            Every(AllButArmour) with
            {
                LeftOutSources = new HashSet<string> { "shop" },
            },
            comparing: false
        );
        Assert.DoesNotContain(Id("RuneShield"), Equipped(shop));
        Assert.Contains(Id("DragonHelmet"), Equipped(shop));
        // An item the data names no source for goes under Other.
        var other = Planner.Plan(
            Sourced(),
            Every(AllButArmour) with
            {
                LeftOutSources = new HashSet<string> { "Other" },
            },
            comparing: false
        );
        Assert.DoesNotContain(Id("IronHelmet"), Equipped(other));
        Assert.Contains(Id("RuneShield"), Equipped(other));
        // An item with a second source stays.
        var both = Sourced() with
        {
            Items =
            [
                .. Sourced()
                    .Items.Select(i => i.Id == Id("RuneShield") ? i with { SourceNames = ["Shop", "Golbin"] } : i),
            ],
        };
        Assert.Contains(
            Id("RuneShield"),
            Equipped(
                Planner.Plan(
                    both,
                    Every(AllButArmour) with
                    {
                        LeftOutSources = new HashSet<string> { "Shop" },
                    },
                    comparing: false
                )
            )
        );
    }

    [Fact]
    public void ThePoolsCountsFollowItsNarrowing()
    {
        var all = PoolCount.Of(Sourced(), Every());
        var wearable = PoolCount.Of(Sourced(), Every() with { WearableOnly = true });
        Assert.Equal(6, all.Single(c => c.SlotType == "Helmet").Items);
        Assert.Equal(5, wearable.Single(c => c.SlotType == "Helmet").Items);
    }

    [Fact]
    public void TheOwnedPoolPlansAsBefore()
    {
        foreach (var comparing in new[] { true, false })
        {
            var before = Planner.Plan(Catalogue(), Request(AllButArmour), comparing);
            var owned = Planner.Plan(Catalogue(), Request(AllButArmour) with { Pool = ItemPool.Owned }, comparing);
            Assert.Equal(before.Possible, owned.Possible);
            Assert.Equal(
                before.Candidates.Select(c => c.Changes.ShortForm),
                owned.Candidates.Select(c => c.Changes.ShortForm)
            );
            Assert.Equal(
                before.LeftOut.Select(l => (l.What, l.Why.English())),
                owned.LeftOut.Select(l => (l.What, l.Why.English()))
            );
            var every = Planner.Plan(Catalogue(), Every(AllButArmour), comparing);
            Assert.True(every.Possible > owned.Possible);
        }
    }

    [Fact]
    public void AWeaponThatIsItsOwnAmmoNeedsNone()
    {
        var thrown = new WeaponPart("Javelin", "Ranged", [Id("Accurate")], AmmoType: "Javelins");
        var javelin = new GearItem(Id("MeteoriteJavelin"), "MeteoriteJavelin", "Weapon")
        {
            Owned = 50,
            Stackable = true,
            Weapon = thrown,
            AmmoType = "Javelins",
        };
        Assert.Null(javelin.AmmoNeeded);
        Assert.Equal("Arrow", Catalogue().Item(Id("IronBow"))!.AmmoNeeded);
        var catalogue = Catalogue();
        catalogue = catalogue with { Items = [.. catalogue.Items, javelin] };
        var plan = Planner.Plan(catalogue, Request() with { AllStyles = true }, comparing: false);
        Assert.Contains(plan.Branches, b => b.Weapon == Id("MeteoriteJavelin"));
        Assert.DoesNotContain(plan.LeftOut, l => l.What == "MeteoriteJavelin");
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
    public void ASetupIgnoringRequirementsTriesGearItOwnsButCantWearYet()
    {
        // Its fights ignore the requirements, so leaving that gear out would leave nothing it can't fight with.
        var request = Request(AllButWeapon);
        request.Setup.IgnoreRequirements = true;
        var plan = Planner.Plan(SwordLocked(), request, comparing: false);
        Assert.Contains(Id("SteelSword"), Equipped(plan));
        Assert.DoesNotContain(plan.LeftOut, l => l.What == "SteelSword");
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
