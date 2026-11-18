using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class KeptSnapshotsTests
{
    private static readonly DateTime Start = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

    private static SnapshotFile At(int minute, long bytes = 700_000) =>
        new($"run-{minute}", Start.AddMinutes(minute), bytes);

    [Fact]
    public void KeepsTheFiveNewest()
    {
        var removed = KeptSnapshots.ToRemove([.. Enumerable.Range(0, 7).Select(m => At(m))], 0, long.MaxValue);

        Assert.Equal(["run-0", "run-1"], removed);
    }

    [Fact]
    public void TheDumpsTakeTheirShareOfTheSpaceFirst()
    {
        var removed = KeptSnapshots.ToRemove([At(0), At(1), At(2)], dumpBytes: 3_000_000, keepBytes: 4_500_000);

        Assert.Equal(["run-0"], removed);
    }

    [Fact]
    public void NothingFitsWhenTheDumpsFillTheSpace()
    {
        var removed = KeptSnapshots.ToRemove([At(0), At(1)], dumpBytes: 5_000_000, keepBytes: 5_000_000);

        Assert.Equal(["run-0", "run-1"], removed);
    }

    [Fact]
    public void NothingToRemove()
    {
        Assert.Empty(KeptSnapshots.ToRemove([At(0)], 0, 10_000_000));
    }
}
