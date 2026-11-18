using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunFileMasterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-tests-" + Guid.NewGuid().ToString("N"));

    public RunFileMasterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void AMastersPartsBlocksAndFactsRoundTrip()
    {
        var run = ResultsModelTests.SampleRun();
        var master = MetricsMasterTests.Master(
            MasterFiguresTests.Part("MelvorBase:CorruptedFarmlands/MelvorBase:Scarecrow", 10, 5),
            MasterFiguresTests.Part("MelvorBase:CorruptedFarmlands/MelvorBase:JackOLantern", 30, 20)
        );
        master.Blocked = ["MelvorBase:GolbinForest/MelvorBase:Golbin"];
        master.Master = new MasterFacts
        {
            BlockCost = 1000,
            Slots =
            [
                new BlockSlotFacts { Id = "MelvorBase:KurenBlock1", Open = true },
                new BlockSlotFacts
                {
                    Id = "MelvorBase:KurenBlock2",
                    Open = false,
                    TasksNeeded = 12,
                },
            ],
        };
        run.Targets.Add(master.Key);
        run.Results[master.Key] = master;
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        var back = RunFile.Read(path).Results[master.Key];
        Assert.Equal(TargetKind.Master, back.Kind);
        Assert.Equal(2, back.Parts!.Count);
        Assert.Equal(master.Parts![1].Batches.Sum(b => b.GameSeconds), back.Parts[1].Batches.Sum(b => b.GameSeconds));
        Assert.Equal(master.Blocked, back.Blocked);
        Assert.Equal(1000, back.Master!.BlockCost);
        Assert.Equal(1, back.Master.OpenSlots);
        Assert.Equal(12, back.Master.Slots[1].TasksNeeded);
        Assert.Equal(
            Metrics.Estimate(master, Metrics.Kills, TimeUnit.Hour),
            Metrics.Estimate(back, Metrics.Kills, TimeUnit.Hour)
        );
    }

    [Fact]
    public void ARunSavedBeforeMastersHasNoneOfTheirFields()
    {
        var run = RunFile.Read(RunFileFormatTests.Format2Run);
        Assert.All(
            run.Results.Values,
            r =>
            {
                Assert.Null(r.Parts);
                Assert.Null(r.Blocked);
                Assert.Null(r.Master);
            }
        );
    }
}
