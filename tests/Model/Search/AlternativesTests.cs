using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class AlternativesTests
{
    private static Estimate E(double point, double low, double high) => new(point, low, high, 30, "paired");

    private static IReadOnlyList<SupplyCost> Uses(params (string Key, double PerHour)[] costs) =>
        [.. costs.Select(c => new SupplyCost(c.Key, E(c.PerHour, c.PerHour, c.PerHour)))];

    private static AlternativePicks.Fought Set(
        int index,
        double gain,
        IReadOnlyList<SupplyCost> supplies,
        double taken
    ) => new(index, E(gain, gain - 1, gain + 1), supplies, E(taken, taken, taken));

    private static readonly AlternativePicks.Fought Best = new(
        1,
        E(10, 8, 12),
        Uses(("used:prayer", 1000), ("used:ammo", 300)),
        E(5, 5, 5)
    );

    [Fact]
    public void CheaperUsesFewerSuppliesWithinTheBestsRange()
    {
        var (cheaper, _) = AlternativePicks.Pick(
            Best,
            [
                Set(2, 9, Uses(("used:prayer", 600), ("used:ammo", 300)), 5),
                Set(3, 9.5, Uses(("used:prayer", 400), ("used:ammo", 300)), 5),
                // Fewer still, but outside the range.
                Set(4, 6, Uses(("used:ammo", 100)), 5),
                // Trades one supply for another: not fewer.
                Set(5, 10, Uses(("used:prayer", 100), ("used:ammo", 400)), 5),
            ]
        );
        Assert.Equal(3, cheaper);
    }

    [Fact]
    public void SaferTakesTheLeastDamageWithinTheRange()
    {
        var (_, safer) = AlternativePicks.Pick(
            Best,
            [Set(2, 9, Best.Supplies, 4), Set(3, 8.5, Best.Supplies, 3), Set(4, 2, Best.Supplies, 1)]
        );
        Assert.Equal(3, safer);
    }

    [Fact]
    public void NoneWhenTheBestAlreadyIsIt()
    {
        var (cheaper, safer) = AlternativePicks.Pick(
            Best,
            [Set(2, 9, Uses(("used:prayer", 2000), ("used:ammo", 300)), 6)]
        );
        Assert.Null(cheaper);
        Assert.Null(safer);
    }
}
