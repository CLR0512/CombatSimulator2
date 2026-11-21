using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class AssumptionTests
{
    // The notes every result carries, in English and in order: result hashes and saved runs depend on them.
    [Fact]
    public void TheNotesAreTheOnesEveryResultCarries()
    {
        Assert.Equal(
            [
                "clean start (combat stopped, HP full)",
                "no item loss on death",
                "fights again after a death",
                "unlimited consumables (topped up after each tick)",
                "levels held (XP reset after each trial)",
                "loot cleared from the bank after it is counted",
                "slayer task as in the save, for the whole run (it never runs out), auto-slayer off",
                "time of day frozen at 12:00",
            ],
            Assumption.All.Select(a => a.Note.English())
        );
    }

    [Fact]
    public void KeysAreUnique() =>
        Assert.Equal(Assumption.All.Count, Assumption.All.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void FourAreShownAsInEveryFight() =>
        Assert.Equal(
            [Assumption.FullHp, Assumption.NoItemLoss, Assumption.Consumables, Assumption.Levels],
            Assumption.All.Where(a => a.InEveryFight).Select(a => a.Key)
        );

    [Fact]
    public void AMastersListDropsTheSavesTaskAndAddsItsOwn()
    {
        var keys = Assumption.ForMaster.Select(a => a.Key).ToList();
        Assert.DoesNotContain(Assumption.SlayerOff, keys);
        Assert.Contains("auto-slayer", keys);
        Assert.Contains("locked-areas", keys);
        Assert.Contains("hour-a-kill", keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        // A monster's own list keeps the save's task and none of the master's.
        Assert.Contains(Assumption.SlayerOff, Assumption.All.Select(a => a.Key));
        Assert.Empty(Assumption.All.Intersect(Assumption.MasterAll));
        // Free swaps are listed only for a master with loadouts per monster, never for every master.
        Assert.DoesNotContain(Assumption.FreeSwaps.Key, keys);
    }
}
