using MelvorMods.CombatSimulator2.Common;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ByteMaskTests
{
    private static byte[] With(params ulong[] values)
    {
        var bytes = new List<byte> { 7, 7, 7 };
        foreach (var value in values)
        {
            bytes.AddRange(BitConverter.GetBytes(value));
            bytes.Add(9);
        }
        return [.. bytes];
    }

    [Fact]
    public void AValueWrittenOnceIsZeroedAndNothingElseMoves()
    {
        var bytes = With(1234, 40975, 5);
        Assert.True(ByteMask.ZeroOnce(bytes, 40975));
        Assert.Equal(With(1234, 0, 5), bytes);
    }

    [Fact]
    public void TwoWritesOfOneValueGiveTheSameBytesOnceItIsTakenOut()
    {
        var earlier = With(1234, 40975, 5);
        var later = With(1234, 41455, 5);
        ByteMask.ZeroOnce(earlier, 40975);
        ByteMask.ZeroOnce(later, 41455);
        Assert.Equal(earlier, later);
    }

    [Fact]
    public void AValueFoundTwiceOrNotAtAllLeavesTheBytesAsTheyAre()
    {
        var twice = With(40975, 3, 40975);
        Assert.False(ByteMask.ZeroOnce(twice, 40975));
        Assert.Equal(With(40975, 3, 40975), twice);
        var none = With(1, 2, 3);
        Assert.False(ByteMask.ZeroOnce(none, 40975));
        Assert.Equal(With(1, 2, 3), none);
    }

    [Fact]
    public void AnOverlappingSecondMatchCountsAsASecond()
    {
        // Zero is found at every offset of a run of zero bytes: never taken out.
        var zeros = new byte[12];
        Assert.False(ByteMask.ZeroOnce(zeros, 0));
    }
}
