using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class GearPlanTextTests
{
    [Fact]
    public void EveryItemCountsEachSlotTypeFirst()
    {
        var plan = new PlanPreview
        {
            Possible = 10,
            Climb = true,
            Pool = ItemPool.Every,
            PoolCounts = [new("MelvorBase:Helmet", 88, 12, 61), new("MelvorBase:Ring", 31, 2, 20)],
        };
        var lines = GearPlanText.Of(plan).Split('\n');
        Assert.Equal("pool: every item", lines[0]);
        Assert.Equal("Helmet: 88 (owned 12, wearable 61)", lines[1]);
        Assert.Equal("Ring: 31 (owned 2, wearable 20)", lines[2]);
        Assert.StartsWith("sets: 10 possible", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void AClimbSaysHowLongItIsExpectedToTake()
    {
        var plan = new PlanPreview
        {
            Climb = true,
            ClimbSeconds = 412.3,
            LimitSeconds = 300,
        };
        var text = GearPlanText.Of(plan);
        Assert.Contains(
            "\na slot at a time: too many sets to fight them all\nexpected: about 413 s",
            text,
            StringComparison.Ordinal
        );
        // Without the player's limit nothing stops it; with one it would pass, the plan says where it stops.
        Assert.DoesNotContain("over the limit", text, StringComparison.Ordinal);
        Assert.Contains(
            "\nover the limit: it stops at 300 s and keeps the best set found",
            GearPlanText.Of(plan with { MaxSeconds = 300 }),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "over the limit",
            GearPlanText.Of(plan with { MaxSeconds = 600 }),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("expected", GearPlanText.Of(plan with { ClimbSeconds = null }), StringComparison.Ordinal);
    }

    [Fact]
    public void TheOwnedPoolPrintsAsBefore() =>
        Assert.StartsWith("sets: ", GearPlanText.Of(new PlanPreview { Possible = 10 }), StringComparison.Ordinal);

    [Fact]
    public void ThePoolIsCountedBySlotTypeWithoutWhatIsExcluded()
    {
        var request = SearchRequestTests.Request() with
        {
            Pool = ItemPool.Every,
            Excluded = new HashSet<string> { Id("DragonHelmet") },
        };
        var counts = PoolCount.Of(Catalogue(), request);
        // One row per type, the two ring slots as one.
        Assert.Equal(11, counts.Count);
        var helmets = counts.Single(c => c.SlotType == "Helmet");
        Assert.Equal(new PoolCount("Helmet", 5, 5, 5), helmets);
        var bodies = counts.Single(c => c.SlotType == "Platebody");
        Assert.Equal(new PoolCount("Platebody", 5, 5, 4), bodies);
        var offhands = counts.Single(c => c.SlotType == "Offhand");
        Assert.Equal(new PoolCount("Offhand", 4, 3, 4), offhands);
        Assert.Equal("Weapon", counts[0].SlotType);
    }
}
