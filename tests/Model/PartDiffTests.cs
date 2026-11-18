using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PartDiffTests
{
    private static SortedDictionary<string, string> Map(params (string Name, string Hash)[] parts)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, hash) in parts)
            map[name] = hash;
        return map;
    }

    [Fact]
    public void EqualMapsDifferInNothing()
    {
        var a = Map(("entity Bank", "aa"), ("rng", "bb"));
        Assert.Empty(PartDiff.Between(a, Map(("rng", "bb"), ("entity Bank", "aa"))));
    }

    [Fact]
    public void AChangedPartIsListed()
    {
        var before = Map(("entity Bank", "aa"), ("component Combat.Health", "cc"), ("rng", "bb"));
        var after = Map(("entity Bank", "aa"), ("component Combat.Health", "cd"), ("rng", "bb"));
        Assert.Equal(["component Combat.Health"], PartDiff.Between(before, after));
    }

    [Fact]
    public void PartsOnOneSideOnlyAreListedInOrder()
    {
        var before = Map(("b", "1"), ("c", "2"));
        var after = Map(("a", "0"), ("b", "1"));
        Assert.Equal(["a", "c"], PartDiff.Between(before, after));
    }

    [Fact]
    public void ExcludedPartsAreIgnored()
    {
        var before = Map(("entity Bank", "aa"), ("rng", "bb"), ("only before", "x"));
        var after = Map(("entity Bank", "ab"), ("rng", "bc"));
        Assert.Equal(
            ["entity Bank"],
            PartDiff.Between(before, after, new HashSet<string>(["rng", "only before"], StringComparer.Ordinal))
        );
    }
}
