using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SideStateTests
{
    [Theory]
    [InlineData("current", SideBaseKind.Current, null)]
    [InlineData("", SideBaseKind.Current, null)]
    [InlineData("MelvorBase:Loadout2", SideBaseKind.Loadout, "MelvorBase:Loadout2")]
    [InlineData("run:run-20261109-064515-1", SideBaseKind.SavedRun, "run-20261109-064515-1")]
    public void ABaseReadsFromItsText(string text, SideBaseKind kind, string? id)
    {
        var read = SideBase.Parse(text);
        Assert.Equal(kind, read.Kind);
        Assert.Equal(id, read.Id);
        Assert.Equal(read, SideBase.Parse(read.Text));
    }

    [Fact]
    public void ASavedRunCarriesNoChanges()
    {
        var changes = ChangeList.Parse("level=Strength:60");
        Assert.Throws<ArgumentException>(() => new SideState(SideBase.SavedRun("r1"), changes));
        var onLoadout = new SideState(SideBase.Loadout("MelvorBase:Loadout2"), changes, true);
        var onRun = onLoadout.WithBase(SideBase.SavedRun("r1"));
        Assert.True(onRun.Changes.IsEmpty);
        Assert.True(onRun.IgnoreRequirements);
        Assert.False(onRun.CanChange);
        Assert.Throws<InvalidOperationException>(() => onRun.ToSetup("Dev"));
    }

    [Fact]
    public void ASideBecomesTheSetupARunUses()
    {
        var side = new SideState(
            SideBase.Loadout("MelvorBase:Loadout2"),
            ChangeList.Parse("equip=Weapon:Iron_Sword level=Strength:60"),
            ignoreRequirements: true
        );
        var setup = side.ToSetup("Dev", "Ranged");
        Assert.Equal(
            "Dev/MelvorBase:Loadout2/ignore-req/changes:level=Strength:60 equip=Weapon:Iron_Sword",
            setup.Identity
        );
        Assert.Equal("Ranged", setup.Name);
        Assert.Equal("Dev/current", SideState.AsNow.ToSetup("Dev", "ignored").Identity);
        Assert.Null(SideState.AsNow.ToSetup("Dev").Changes);
    }

    [Fact]
    public void SidesAreEqualByBaseChangesAndFlag()
    {
        var a = new SideState(SideBase.Current, ChangeList.Parse("food=Shrimp level=Attack:40"));
        var same = new SideState(SideBase.Parse("current"), ChangeList.Parse("level=Attack:40 food=Shrimp"));
        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, a.WithIgnoreRequirements(true));
        Assert.NotEqual(a, a.WithBase(SideBase.Loadout("MelvorBase:Loadout2")));
        Assert.NotEqual(a, a.WithChanges(ChangeList.Empty));
    }

    [Fact]
    public void SwapExchangesTheSidesWhole()
    {
        var a = new SideState(SideBase.Current, ChangeList.Parse("level=Defence:60"));
        var b = new SideState(SideBase.Loadout("MelvorBase:Loadout2"), null, true);
        var (newA, newB) = SideState.Swap(a, b);
        Assert.Same(b, newA);
        Assert.Same(a, newB);
    }

    [Fact]
    public void ABaseIsWrittenAsItsText()
    {
        var json = JsonSerializer.Serialize(SideBase.SavedRun("r1"));
        Assert.Equal("\"run:r1\"", json);
        Assert.Equal(SideBase.SavedRun("r1"), JsonSerializer.Deserialize<SideBase>(json));
    }

    [Fact]
    public void PickingAnotherStartClearsTheChangesAndKeepsRequirements()
    {
        var side = new SideState(SideBase.Loadout("L2"), ChangeList.Parse("level=Strength:50"), true);
        var picked = side.StartingFrom(SideBase.Current);
        Assert.Equal(SideBase.Current, picked.Base);
        Assert.True(picked.Changes.IsEmpty);
        Assert.True(picked.IgnoreRequirements);
        Assert.Same(side, side.StartingFrom(SideBase.Loadout("L2")));
    }
}
