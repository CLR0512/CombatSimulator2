using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class EquipChangeBehindTests
{
    private const string Weapon = "MelvorBase:Weapon";
    private const string Shield = "MelvorBase:Shield";
    private const string Helmet = "MelvorBase:Helmet";
    private const string Bow = "MelvorBase:Harpy_Bow";
    private const string Sword = "MelvorBase:Iron_Sword";
    private const string Buckler = "MelvorBase:Iron_Shield";
    private const string Hood = "MelvorBase:Leather_Hood";

    private static Dictionary<string, string?> Slots(string? weapon, string? shield, string? helmet = Hood) =>
        new(StringComparer.Ordinal)
        {
            [Weapon] = weapon,
            [Shield] = shield,
            [Helmet] = helmet,
        };

    [Theory]
    [InlineData(Weapon)]
    [InlineData(Shield)]
    public void EmptyingATwoHanderFromEitherSlotIsFoundFromBoth(string emptied)
    {
        var changes = ChangeList.Empty.With(new Change(ChangeKind.Equip, emptied, Change.None));
        var before = Slots(Bow, Bow);
        var after = Slots(null, null);

        Assert.Equal(emptied, changes.EquipChangeBehind(Weapon, before, after)?.Subject);
        Assert.Equal(emptied, changes.EquipChangeBehind(Shield, before, after)?.Subject);
    }

    [Fact]
    public void EquippingATwoHanderIsFoundFromTheSlotItPushedOut()
    {
        var changes = ChangeList.Empty.With(new Change(ChangeKind.Equip, Weapon, Bow));
        var before = Slots(Sword, Buckler);
        var after = Slots(Bow, Bow);

        Assert.Equal(Weapon, changes.EquipChangeBehind(Shield, before, after)?.Subject);
    }

    [Fact]
    public void AShieldOverATwoHanderIsFoundFromTheWeaponSlot()
    {
        var changes = ChangeList.Empty.With(new Change(ChangeKind.Equip, Shield, Buckler));
        var before = Slots(Bow, Bow);
        var after = Slots(null, Buckler);

        Assert.Equal(Shield, changes.EquipChangeBehind(Weapon, before, after)?.Subject);
    }

    [Fact]
    public void ASlotThatDidNotChangeHasNone()
    {
        var changes = ChangeList.Empty.With(new Change(ChangeKind.Equip, Weapon, Change.None));
        var before = Slots(Bow, Bow);
        var after = Slots(null, null);

        Assert.Null(changes.EquipChangeBehind(Helmet, before, after));
    }

    [Fact]
    public void AnotherSlotsChangeIsNotTakenForAnUnrelatedOne()
    {
        var changes = ChangeList
            .Empty.With(new Change(ChangeKind.Equip, Weapon, Sword))
            .With(new Change(ChangeKind.Equip, Helmet, Change.None));
        var before = Slots(Bow, Bow);
        var after = Slots(Sword, null, helmet: null);

        Assert.Equal(Helmet, changes.EquipChangeBehind(Helmet, before, after)?.Subject);
        Assert.Equal(Weapon, changes.EquipChangeBehind(Shield, before, after)?.Subject);
    }

    [Fact]
    public void ASlotsOwnChangeComesFirst()
    {
        var changes = ChangeList.Empty.With(new Change(ChangeKind.Equip, Helmet, Hood));

        Assert.Equal(Helmet, changes.EquipChangeBehind(Helmet, Slots(Bow, Bow), Slots(Bow, Bow))?.Subject);
    }
}
