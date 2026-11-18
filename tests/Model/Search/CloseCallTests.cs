using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class CloseCallTests
{
    private static Estimate D(double low, double high) => new((low + high) / 2, low, high, 30, "paired");

    [Theory]
    [InlineData("equip=RingSlot1:RubyRing", "equip=RingSlot1:SapphireRing", true)]
    [InlineData("prayer=A:on prayer=B:on", "prayer=A:on prayer=C:on", true)]
    [InlineData("equip=RingSlot1:RubyRing", "equip=RingSlot1:SapphireRing food=Trout", false)]
    [InlineData("equip=RingSlot1:RubyRing", "equip=RingSlot1:RubyRing", false)]
    public void ACloseCallDiffersInOneSlotOrChoice(string a, string b, bool close) =>
        Assert.Equal(close, CloseCalls.IsCloseCall(ChangeList.Parse(a), ChangeList.Parse(b)));

    [Fact]
    public void AClearWinSwaps() =>
        Assert.Equal((CloseCallOutcome.Swapped, true), CloseCalls.Settle(D(1, 3), false, false, false, 0, 0));

    [Fact]
    public void AClearLossKeeps() =>
        Assert.Equal((CloseCallOutcome.Kept, false), CloseCalls.Settle(D(-3, -1), false, false, false, 0, 0));

    [Fact]
    public void LowerIsBetterTurnsTheDifferenceRound() =>
        Assert.Equal((CloseCallOutcome.Swapped, true), CloseCalls.Settle(D(-3, -1), true, false, false, 0, 0));

    [Fact]
    public void ATieKeepsTheSetupsOwnOption()
    {
        Assert.Equal((CloseCallOutcome.Tie, true), CloseCalls.Settle(D(-1, 1), false, false, true, 5, 0));
        Assert.Equal((CloseCallOutcome.Tie, false), CloseCalls.Settle(D(-1, 1), false, true, false, 0, 5));
    }

    [Fact]
    public void ElseATieKeepsTheOneOwnedMore()
    {
        Assert.Equal((CloseCallOutcome.Tie, true), CloseCalls.Settle(D(-1, 1), false, false, false, 1, 3));
        Assert.Equal((CloseCallOutcome.Tie, false), CloseCalls.Settle(D(-1, 1), false, false, false, 3, 3));
    }

    [Fact]
    public void OwnedCountsTheScarcerPieceOfTheOptionAndNothingElse()
    {
        var keys = new[]
        {
            new ChangeKey(ChangeKind.Equip, "MelvorBase:RingSlot1"),
            new ChangeKey(ChangeKind.Equip, "MelvorBase:Helmet"),
        };
        var counts = new Dictionary<string, int> { ["MelvorBase:RubyRing"] = 4, ["MelvorBase:IronHelmet"] = 2 };
        int Owned(string id) => counts.GetValueOrDefault(id);
        var both = ChangeList.Parse("equip=RingSlot1:RubyRing equip=Helmet:IronHelmet");
        Assert.Equal(2, CloseCalls.Owned(both, keys, Owned));
        Assert.Equal(0, CloseCalls.Owned(ChangeList.Parse("prayer=A:on"), keys, Owned));
        Assert.Equal(0, CloseCalls.Owned(ChangeList.Empty, keys, Owned));
    }
}
