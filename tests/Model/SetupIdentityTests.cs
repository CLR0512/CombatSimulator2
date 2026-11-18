using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SetupIdentityTests
{
    // Saved runs and reuse keys hold these exact strings: a setup without changes must keep them.
    [Fact]
    public void WithoutChangesTheIdentityIsAsBefore()
    {
        Assert.Equal("Dev/current", new Setup { Character = "Dev" }.Identity);
        Assert.Equal("Dev/current", new Setup { Character = "Dev", Changes = ChangeList.Empty }.Identity);
        Assert.Equal(
            "Dev/MelvorBase:Loadout2/ignore-req",
            new Setup
            {
                Character = "Dev",
                Loadout = "MelvorBase:Loadout2",
                IgnoreRequirements = true,
                Changes = null,
            }.Identity
        );
    }

    [Fact]
    public void ChangesAreAppended()
    {
        var setup = new Setup
        {
            Character = "Dev",
            IgnoreRequirements = true,
            Changes = ChangeList.Parse("level=Strength:60 equip=Weapon:Iron_Sword"),
        };
        Assert.Equal("Dev/current/ignore-req/changes:level=Strength:60 equip=Weapon:Iron_Sword", setup.Identity);
    }

    // Written before the kinds a Setup page changes existed; those kinds sort between these and never reorder them.
    [Fact]
    public void AnIdentityWithTheFirstKindsIsAsBefore()
    {
        var setup = new Setup
        {
            Character = "Dev",
            Changes = ChangeList.Parse(
                "prayer=Thick_Skin:on potion=none food=Shrimp imbuement=none spell=AirBolt stance=Stab "
                    + "equip=Weapon:Iron_Sword level=Strength:60"
            ),
        };
        Assert.Equal(
            "Dev/current/changes:level=Strength:60 equip=Weapon:Iron_Sword stance=Stab spell=AirBolt "
                + "imbuement=none food=Shrimp potion=none prayer=Thick_Skin:on",
            setup.Identity
        );
    }

    [Fact]
    public void ChangesOnTheFirstSideAreInItsIdentity()
    {
        var plain = new Setup { Character = "Dev" };
        var changed = new Setup { Character = "Dev", Changes = ChangeList.Parse("star=Terra_Minor_3:on") };
        Assert.NotEqual(plain.Identity, changed.Identity);
        Assert.Equal("Dev/current/changes:star=Terra_Minor_3:on", changed.Identity);
    }

    [Fact]
    public void TheSameChangesInAnyOrderAreOneIdentity()
    {
        var a = new Setup
        {
            Character = "Dev",
            Changes = ChangeList.Empty.With(Change.Parse("food=Shrimp")).With(Change.Parse("level=Attack:40")),
        };
        var b = new Setup
        {
            Character = "Dev",
            Changes = ChangeList.Empty.With(Change.Parse("level=Attack:40")).With(Change.Parse("food=Shrimp")),
        };
        Assert.Equal(a.Identity, b.Identity);
    }

    [Fact]
    public void NameAndLabelIgnoreChanges()
    {
        var plain = new Setup { Character = "Dev", IgnoreRequirements = true };
        var changed = new Setup
        {
            Character = "Dev",
            IgnoreRequirements = true,
            Changes = ChangeList.Parse("level=Strength:60"),
        };
        Assert.Equal(plain.Name, changed.Name);
        Assert.Equal(plain.Label, changed.Label);
    }

    [Fact]
    public void AnEmptyListIsStoredAsNone() => Assert.Null(new Setup { Changes = ChangeList.Empty }.Changes);
}
