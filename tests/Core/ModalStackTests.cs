using MelvorMods.Core;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ModalStackTests
{
    private readonly ModalStack _stack = new();
    private readonly List<(string Id, bool Open)> _changes = [];

    public ModalStackTests() => _stack.Changed += (id, open) => _changes.Add((id, open));

    [Fact]
    public void OpenCloseAndToggleKeepAStack()
    {
        _stack.Open("a");
        _stack.Open("b");
        Assert.Equal(["a", "b"], _stack.Opened);
        Assert.True(_stack.IsOpen("a"));

        Assert.False(_stack.Toggle("b"));
        Assert.Equal(["a"], _stack.Opened);
        Assert.True(_stack.Toggle("c"));
        Assert.Equal(["a", "c"], _stack.Opened);

        Assert.True(_stack.Close("a"));
        Assert.False(_stack.Close("a"));
        Assert.Equal(["c"], _stack.Opened);
        Assert.Equal([("a", true), ("b", true), ("b", false), ("c", true), ("a", false)], _changes);
    }

    [Fact]
    public void OpeningAnOpenModalBringsItToTheTop()
    {
        _stack.Open("a");
        _stack.Open("b");
        _stack.Open("a");
        Assert.Equal(["b", "a"], _stack.Opened);
        Assert.Equal(2, _changes.Count);
    }

    [Fact]
    public void CancelClosesOnlyTheTopAndIsHandled()
    {
        _stack.Open("a");
        _stack.Open("b");
        Assert.True(_stack.Cancel());
        Assert.Equal(["a"], _stack.Opened);
        Assert.True(_stack.Cancel());
        Assert.Empty(_stack.Opened);
    }

    [Fact]
    public void CancelWithNothingOpenIsNotHandled()
    {
        Assert.False(_stack.Cancel());
        Assert.Empty(_changes);
    }

    [Fact]
    public void ThePauseMenuClosesEveryModal()
    {
        _stack.Open("a");
        _stack.Open("b");
        _stack.CloseAll();
        Assert.Empty(_stack.Opened);
        Assert.Equal([("a", true), ("b", true), ("b", false), ("a", false)], _changes);
        _stack.CloseAll();
        Assert.Equal(4, _changes.Count);
    }

    [Fact]
    public void RemovingAModalForgetsItSilently()
    {
        _stack.Open("a");
        _stack.Open("b");
        _stack.Remove("a");
        Assert.Equal(["b"], _stack.Opened);
        Assert.False(_stack.IsOpen("a"));
        Assert.Equal(2, _changes.Count);
    }

    [Fact]
    public void AnEscapeHookThatHandlesTheKeyKeepsTheModalOpen()
    {
        var asked = 0;
        _stack.Open("a");
        _stack.SetEscape("a", () => ++asked > 0);
        Assert.True(_stack.Cancel());
        Assert.Equal(["a"], _stack.Opened);
        Assert.Equal(1, asked);
        Assert.Single(_changes);
    }

    [Fact]
    public void AnEscapeHookThatDeclinesLetsTheModalClose()
    {
        _stack.Open("a");
        _stack.SetEscape("a", () => false);
        Assert.True(_stack.Cancel());
        Assert.Empty(_stack.Opened);
    }

    [Fact]
    public void AThrowingEscapeHookIsReportedAndTheModalCloses()
    {
        var failures = new List<string>();
        _stack.EscapeFailed += (id, _) => failures.Add(id);
        _stack.Open("a");
        _stack.SetEscape("a", () => throw new InvalidOperationException("broken"));
        Assert.True(_stack.Cancel());
        Assert.Empty(_stack.Opened);
        Assert.Equal(["a"], failures);
    }

    [Fact]
    public void OnlyTheTopModalsEscapeHookIsAsked()
    {
        var askedBelow = false;
        _stack.Open("a");
        _stack.SetEscape("a", () => askedBelow = true);
        _stack.Open("b");
        Assert.True(_stack.Cancel());
        Assert.False(askedBelow);
        Assert.Equal(["a"], _stack.Opened);
    }

    [Fact]
    public void ThePauseMenuAsksNoEscapeHook()
    {
        var asked = false;
        _stack.Open("a");
        _stack.SetEscape("a", () => asked = true);
        _stack.CloseAll();
        Assert.False(asked);
        Assert.Empty(_stack.Opened);
    }

    [Fact]
    public void RemovingAModalForgetsItsEscapeHook()
    {
        var asked = false;
        _stack.SetEscape("a", () => asked = true);
        _stack.Remove("a");
        _stack.Open("a");
        Assert.True(_stack.Cancel());
        Assert.False(asked);
        Assert.Empty(_stack.Opened);
    }
}
