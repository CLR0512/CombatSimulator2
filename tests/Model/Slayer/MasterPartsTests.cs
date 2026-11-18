using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MasterPartsTests
{
    private const string Master = "master:MelvorBase:Kuren";
    private const string Golbin = Master + "/MelvorBase:GolbinForest/MelvorBase:Golbin";
    private const string Cow = Master + "/MelvorBase:CorruptedFarmlands/MelvorBase:MagicCow";

    private static Run RunWithMaster()
    {
        var run = new Run { Targets = [Master, "MelvorBase:GolbinForest/MelvorBase:Golbin"] };
        run.Results[Master] = new TargetResult
        {
            Key = Master,
            Kind = TargetKind.Master,
            State = TargetState.Queued,
            Parts =
            [
                new TargetResult { Key = Golbin, State = TargetState.Queued },
                new TargetResult { Key = Cow, State = TargetState.Queued },
            ],
        };
        run.Results[run.Targets[1]] = new TargetResult { Key = run.Targets[1], State = TargetState.Queued };
        return run;
    }

    private static TargetResult Finished(string key)
    {
        var part = MasterFiguresTests.Part(key, 10, 5);
        part.Assumptions = [.. Assumption.All.Select(a => a.Note), Note.Of("requirements ignored: {0}", "Slayer 30")];
        return part;
    }

    [Fact]
    public void APartIsFoundInItsMasterAndNeverAsTheMonstersOwnRow()
    {
        var run = RunWithMaster();
        Assert.Same(run.Results[Master].Parts![0], MasterParts.Find(run, Golbin));
        Assert.Same(run.Results[run.Targets[1]], MasterParts.Find(run, run.Targets[1]));
        Assert.Null(MasterParts.Find(run, Master + "/MelvorBase:Nowhere/MelvorBase:None"));
    }

    [Fact]
    public void TheMasterFinishesWhenEveryPartHasWithItsOwnAssumptions()
    {
        var run = RunWithMaster();
        Assert.True(MasterParts.Put(run, Finished(Golbin)));
        var master = run.Results[Master];
        Assert.Equal(TargetState.Fighting, master.State);
        Assert.True(MasterParts.Put(run, Finished(Cow)));
        Assert.Equal(TargetState.Finished, master.State);
        Assert.Equal(600, master.TrialsDone);
        var notes = master.Assumptions.Select(n => n.English()).ToList();
        Assert.Contains("Auto Slayer on, starting in the task's area", notes);
        Assert.DoesNotContain(Assumption.All.Single(a => a.Key == Assumption.SlayerOff).Note.English(), notes);
        Assert.Single(notes, n => n.StartsWith("requirements ignored", StringComparison.Ordinal));
        // The monster's own row is untouched.
        Assert.Equal(TargetState.Queued, run.Results[run.Targets[1]].State);
    }

    // A search fights a master's monsters with a loadout of their own once and fills them into each set's result.
    [Fact]
    public void AMasterFilledInLaterSettlesAsOneFoughtWhole()
    {
        static TargetResult MasterOf(params TargetResult[] parts) =>
            new()
            {
                Key = Master,
                Kind = TargetKind.Master,
                State = TargetState.Queued,
                Parts = [.. parts],
            };
        var whole = MasterOf(Finished(Golbin), Finished(Cow));
        MasterParts.Settle(whole);
        var shared = MasterOf(Finished(Golbin), new TargetResult { Key = Cow, State = TargetState.Queued });
        MasterParts.Settle(shared);
        Assert.False(shared.IsFinal);
        shared.Parts![1] = Finished(Cow);
        MasterParts.Settle(shared);
        Assert.Equal(TargetState.Finished, shared.State);
        Assert.Equal(whole.TrialsDone, shared.TrialsDone);
        Assert.Equal(whole.GameSeconds, shared.GameSeconds);
        Assert.Equal(whole.Assumptions.Select(n => n.English()), shared.Assumptions.Select(n => n.English()));
    }

    [Fact]
    public void FreeSwapsAreAssumedOnlyWhenAMonsterHasALoadoutOfItsOwn()
    {
        var swap = Assumption.FreeSwaps.Note.English();
        var plain = RunWithMaster();
        MasterParts.Put(plain, Finished(Golbin));
        MasterParts.Put(plain, Finished(Cow));
        Assert.DoesNotContain(swap, plain.Results[Master].Assumptions.Select(n => n.English()));
        var swapped = RunWithMaster();
        swapped.Results[Master].Loadouts = new()
        {
            ["MelvorBase:GolbinForest/MelvorBase:Golbin"] = "MelvorBase:Loadout1",
        };
        MasterParts.Put(swapped, Finished(Golbin));
        MasterParts.Put(swapped, Finished(Cow));
        Assert.Single(swapped.Results[Master].Assumptions, n => n.English() == swap);
    }

    [Fact]
    public void AMasterWhoseMonstersAreAllRefusedIsRefusedForTheSameReason()
    {
        var run = RunWithMaster();
        foreach (var key in new[] { Golbin, Cow })
            MasterParts.Put(
                run,
                new TargetResult
                {
                    Key = key,
                    State = TargetState.Refused,
                    Reasons = [Note.Text("Slayer Level 30")],
                }
            );
        Assert.Equal(TargetState.Refused, run.Results[Master].State);
        Assert.Equal("Slayer Level 30", run.Results[Master].Reasons.Single().English());
    }

    [Fact]
    public void EndingAMasterEndsItsUnfinishedParts()
    {
        var run = RunWithMaster();
        MasterParts.Put(run, Finished(Golbin));
        MasterParts.End(run.Results[Master], TargetState.Cancelled, Note.Of("cancelled"));
        Assert.Equal(TargetState.Finished, run.Results[Master].Parts![0].State);
        Assert.Equal(TargetState.Cancelled, run.Results[Master].Parts![1].State);
        Assert.Equal(TargetState.Cancelled, run.Results[Master].State);
    }

    [Fact]
    public void AMastersHashCoversItsPartsAndOtherTargetsHashAsBefore()
    {
        var run = RunWithMaster();
        MasterParts.Put(run, Finished(Golbin));
        MasterParts.Put(run, Finished(Cow));
        var before = ResultsHashInput.Brief([run.Results[Master]]);
        run.Results[Master].Parts![1].Batches[0].Kills++;
        Assert.NotEqual(before, ResultsHashInput.Brief([run.Results[Master]]));
    }
}
