using MelvorMods.CombatSimulator2.Engine;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class DefaultCopiesTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 4)]
    [InlineData(8, 4)]
    [InlineData(16, 4)]
    public void TwoCoresStayFreeUpToFourCopiesAndNeverFewerThanOne(int cores, int copies) =>
        Assert.Equal(copies, DefaultCopies.For(cores));
}
