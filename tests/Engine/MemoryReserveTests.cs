using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MemoryReserveTests
{
    private const long MB = 1024 * 1024;
    private const long GB = 1024 * MB;

    [Fact]
    public void BuildsWhenACopyStillLeavesTheReserve()
    {
        Assert.Equal(ReserveDecision.Build, MemoryReserve.Decide(2 * GB, 150 * MB, GB, 0, false));
        // exactly at the reserve still builds
        Assert.Equal(ReserveDecision.Build, MemoryReserve.Decide(GB + 150 * MB, 150 * MB, GB, 3, true));
    }

    [Fact]
    public void WhenShortPreBuildingStopsFirst()
    {
        // Pre-building just waits, even with nothing else built: a fight slot builds its own if it can.
        Assert.Equal(ReserveDecision.Wait, MemoryReserve.Decide(GB + 100 * MB, 150 * MB, GB, 2, true));
        Assert.Equal(ReserveDecision.Wait, MemoryReserve.Decide(GB, 150 * MB, GB, 0, true));
    }

    [Fact]
    public void WhenShortAFightSlotWaitsWhileAnotherCopyLives()
    {
        Assert.Equal(ReserveDecision.Wait, MemoryReserve.Decide(GB + 100 * MB, 150 * MB, GB, 1, false));
        Assert.Equal(ReserveDecision.Wait, MemoryReserve.Decide(GB + 100 * MB, 150 * MB, GB, 3, false));
    }

    [Fact]
    public void WithNothingBuiltAndOneCopyNotFittingItIsOutOfMemory()
    {
        Assert.Equal(ReserveDecision.OutOfMemory, MemoryReserve.Decide(GB + 100 * MB, 150 * MB, GB, 0, false));
    }

    [Fact]
    public void NoReserveAlwaysBuilds()
    {
        Assert.Equal(ReserveDecision.Build, MemoryReserve.Decide(0, 150 * MB, 0, 0, false));
    }

    [Fact]
    public void TheCopyEstimateStartsAt150MbAndFollowsTheLastBuild()
    {
        var reserve = new MemoryReserve(GB, () => 4 * GB);
        Assert.Equal(150 * MB, reserve.CopyEstimate);
        reserve.Built(210 * MB);
        Assert.Equal(210 * MB, reserve.CopyEstimate);
        reserve.Built(95 * MB);
        Assert.Equal(95 * MB, reserve.CopyEstimate);
        reserve.Built(0); // an unreadable measure keeps the last one
        Assert.Equal(95 * MB, reserve.CopyEstimate);
    }

    [Fact]
    public void TheInstanceAsksWithItsOwnReserveAndAvailableMemory()
    {
        var available = 2 * GB;
        var reserve = new MemoryReserve(GB, () => available);
        Assert.Equal(ReserveDecision.Build, reserve.Decide(0, false));
        available = GB + 100 * MB;
        Assert.Equal(ReserveDecision.OutOfMemory, reserve.Decide(0, false));
        Assert.Equal(ReserveDecision.Wait, reserve.Decide(2, false));
    }

    [Fact]
    public void TheOutOfMemoryReasonSaysWhatIsNeededAndWhatToDo()
    {
        var reason = MemoryReserve.OutOfMemoryReason(150 * MB, GB, GB + 100 * MB);
        Assert.Equal(
            "not enough free memory for one copy (needs about 1174 MB with 1124 MB free): close other programs, "
                + "or simulate in the game's process",
            reason.English()
        );
    }

    [Fact]
    public void ARunStartsWithTheFightSlotsThatFitWhileFighting()
    {
        const long fight = MemoryReserve.FightBytes;
        const long run = MemoryReserve.RunBytes;
        // Everything asked fits: stock copies cost only their fighting.
        Assert.Equal(4, MemoryReserve.Fighters(GB + run + 4 * fight, GB, 4, 4, 150 * MB));
        // One byte short of four leaves three.
        Assert.Equal(3, MemoryReserve.Fighters(GB + run + 4 * fight - 1, GB, 4, 4, 150 * MB));
        // Copies beyond the stock have to be built as well.
        Assert.Equal(4, MemoryReserve.Fighters(GB + run + 4 * fight + 2 * 150 * MB, GB, 4, 2, 150 * MB));
        Assert.Equal(3, MemoryReserve.Fighters(GB + run + 4 * fight + 2 * 150 * MB - 1, GB, 4, 2, 150 * MB));
        // Never below one, however little is free; never more than asked.
        Assert.Equal(1, MemoryReserve.Fighters(0, GB, 4, 0, 150 * MB));
        Assert.Equal(1, MemoryReserve.Fighters(64 * GB, GB, 1, 0, 150 * MB));
    }

    [Fact]
    public void TheStockGrowsOnlyWhileAllOfItCouldFight()
    {
        var available = GB + MemoryReserve.RunBytes + 2 * MemoryReserve.FightBytes + 150 * MB;
        var reserve = new MemoryReserve(GB, () => available);
        Assert.True(reserve.StockFits(1));
        Assert.False(reserve.StockFits(2));
    }
}
