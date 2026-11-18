using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class StatsKeysTests
{
    // The slash stance reads attack and strength; defence is the evasion stat. Magic is read by no stance here.
    private static GearCatalogue Read(Func<GearItem, GearItem>? change = null) =>
        Catalogue(change) with
        {
            StanceReads = new Dictionary<string, IReadOnlySet<string>>
            {
                [Id("Slash")] = new HashSet<string> { Attack, Strength },
            },
            EvasionStats = new HashSet<string> { Defence },
        };

    private static ChangeList Wear(string slot, string item) =>
        ChangeList.Of([new Change(ChangeKind.Equip, Id(slot), Id(item))]);

    [Fact]
    public void StatsTheFightNumbersStandForLeaveTheRestEqual()
    {
        // Iron and steel helmets differ only in defence, which the fight numbers carry.
        var catalogue = Read();
        Assert.Equal(
            StatsKeys.Rest(catalogue, Wear("Helmet", "IronHelmet")),
            StatsKeys.Rest(catalogue, Wear("Helmet", "SteelHelmet"))
        );
    }

    [Fact]
    public void AStatNoStanceReadsAndAModifierKeepSetsApart()
    {
        var catalogue = Read(i =>
            i.Id == Id("SteelHelmet")
                ? i with
                {
                    Bonuses = [new PartModifier("XpPercent", null, 5, Direction.Good, PartGroup.Xp)],
                }
                : i
        );
        var iron = StatsKeys.Rest(catalogue, Wear("Helmet", "IronHelmet"));
        Assert.NotEqual(iron, StatsKeys.Rest(catalogue, Wear("Helmet", "SteelHelmet")));
        Assert.NotEqual(iron, StatsKeys.Rest(catalogue, Wear("Helmet", "MageHat")));
    }

    [Fact]
    public void ItemsThatAddUpToAnotherAreTheSame()
    {
        // Magic, read by no stance here, stays in the key as a total: 2 + 4 on two items is the 6 on one.
        var magic = new Dictionary<string, double>
        {
            [Id("IronHelmet")] = 2,
            [Id("IronPlatebody")] = 4,
            [Id("SteelHelmet")] = 6,
        };
        var catalogue = Read(i =>
            magic.TryGetValue(i.Id, out var value)
                ? i with
                {
                    Stats = new Dictionary<string, double>(i.Stats) { [Magic] = value },
                }
                : i
        );
        var two = ChangeList.Of([
            new Change(ChangeKind.Equip, Id("Helmet"), Id("IronHelmet")),
            new Change(ChangeKind.Equip, Id("Platebody"), Id("IronPlatebody")),
        ]);
        Assert.Equal(StatsKeys.Rest(catalogue, two), StatsKeys.Rest(catalogue, Wear("Helmet", "SteelHelmet")));
        Assert.NotEqual(StatsKeys.Rest(catalogue, two), StatsKeys.Rest(catalogue, Wear("Helmet", "IronHelmet")));
    }

    [Fact]
    public void WhatTheWeaponTakesIsNotWorn()
    {
        // The two-hander empties the offhand: the shield worn now is no part of the set.
        var catalogue = Read();
        var rest = StatsKeys.Rest(catalogue, Wear("Weapon", "Iron2HSword"));
        var bare = StatsKeys.Rest(
            catalogue,
            ChangeList.Of([
                new Change(ChangeKind.Equip, Id("Weapon"), Id("Iron2HSword")),
                new Change(ChangeKind.Equip, Id("Offhand"), Change.None),
            ])
        );
        Assert.Equal(bare, rest);
    }

    [Fact]
    public void EqualKeysMergeIntoTheFirstAndTheRestAreNumberedAgain()
    {
        var a = Wear("Helmet", "IronHelmet");
        var b = Wear("Helmet", "SteelHelmet");
        var c = Wear("Helmet", "MageHat");
        var d = Wear("Platebody", "IronPlatebody");
        Candidate[] candidates =
        [
            new(0, ChangeList.Empty, "Melee") { StatsKey = "same" },
            new(1, a, "Melee") { StatsKey = "other" },
            new(2, b, "Melee") { StatsKey = "same", Same = [d] },
            new(3, c, "Melee") { StatsKey = null },
            new(4, d, "Melee") { StatsKey = "other" },
        ];
        var merged = StatsKeys.Merge(candidates, out var count);
        Assert.Equal(2, count);
        Assert.Equal([0, 1, 2], merged.Select(m => m.Index));
        Assert.True(merged[0].IsCurrent);
        Assert.Equal([b, d], merged[0].Same);
        Assert.Equal(a, merged[1].Changes);
        Assert.Equal([d], merged[1].Same);
        Assert.Equal(c, merged[2].Changes);
    }
}
