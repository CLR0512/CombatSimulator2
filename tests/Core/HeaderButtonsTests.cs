using MelvorMods.Core;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HeaderButtonsTests
{
    private readonly HeaderRegistry _registry = new();
    private int _changes;

    public HeaderButtonsTests() => _registry.Changed += () => _changes++;

    private HeaderEntry Add(string owner, string id) =>
        _registry.Add(new HeaderEntry(owner, id, id.ToUpperInvariant(), null, null, () => { }));

    [Fact]
    public void ButtonsAreShownInAddOrder()
    {
        Add("B", "b");
        Add("A", "a");
        Add("B", "c");
        Assert.Equal(["b", "a", "c"], _registry.Shown.Select(e => e.Id));
        Assert.Equal(3, _changes);
    }

    [Fact]
    public void HidingThenShowingKeepsThePlace()
    {
        Add("A", "a");
        var b = Add("A", "b");
        Add("A", "c");

        _registry.SetVisible(b, false);
        Assert.Equal(["a", "c"], _registry.Shown.Select(e => e.Id));
        _registry.SetVisible(b, true);
        Assert.Equal(["a", "b", "c"], _registry.Shown.Select(e => e.Id));

        _registry.SetVisible(b, true);
        Assert.Equal(5, _changes);
    }

    [Fact]
    public void RemoveForgetsTheButton()
    {
        var a = Add("A", "a");
        Add("A", "b");
        _registry.Remove(a);
        _registry.Remove(a);
        Assert.Equal(["b"], _registry.Shown.Select(e => e.Id));
        _registry.SetVisible(a, true);
        Assert.Equal(["b"], _registry.Shown.Select(e => e.Id));
        Assert.Equal(3, _changes);
    }

    [Fact]
    public void UnloadClearsTheModsButtons()
    {
        Add("A", "a");
        Add("B", "b");
        Add("A", "c");
        Assert.Equal(2, _registry.RemoveOwner("a"));
        Assert.Equal(["b"], _registry.Shown.Select(e => e.Id));
        Assert.Equal(0, _registry.RemoveOwner("A"));
        Assert.Equal(4, _changes);
    }

    [Fact]
    public void ReaddingAnIdReplacesTheOldButton()
    {
        Add("A", "a");
        Add("A", "b");
        Add("A", "a");
        Assert.Equal(["b", "a"], _registry.Shown.Select(e => e.Id));
    }

    [Fact]
    public void NodeNamesAreTheIdPlusButton() =>
        Assert.Equal("Cs2SimButton", new HeaderEntry("A", "Cs2Sim", "Sim", null, null, () => { }).NodeName);

    [Fact]
    public void TheSweepRemovesOnlyThisCopysButtons()
    {
        Add("CombatSimulator2", "Cs2Sim");
        Assert.True(HeaderRegistry.IsOwnButton("CombatSimulator2", _registry.Owners));
        Assert.True(HeaderRegistry.IsOwnButton("combatsimulator2", _registry.Owners));
        Assert.False(HeaderRegistry.IsOwnButton("mElvUI", _registry.Owners));
        Assert.False(HeaderRegistry.IsOwnButton(null, _registry.Owners));
    }

    [Fact]
    public void OwnersStayHostedAfterTheirButtonsGo()
    {
        var a = Add("CombatSimulator2", "Cs2Sim");
        _registry.Remove(a);
        _registry.RemoveOwner("CombatSimulator2");
        Assert.True(HeaderRegistry.IsOwnButton("CombatSimulator2", _registry.Owners));
    }

    [Fact]
    public void ANewIconChangesThePictureWithoutPlacingTheButtonsAgain()
    {
        var a = Add("A", "a");
        var placed = 0;
        HeaderEntry? redrawn = null;
        _registry.Changed += () => placed++;
        _registry.IconChanged += entry => redrawn = entry;
        var icon = new object();
        _registry.SetIcon(a, icon);
        Assert.Equal(0, placed);
        Assert.Same(a, redrawn);
        Assert.Same(icon, a.Icon);
    }
}
