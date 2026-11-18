using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ResultIndexTests
{
    private const string Golbin = "MelvorBase:GolbinForest/MelvorBase:Golbin";
    private const string Camp = "MelvorBase:HobgolbinCamp";

    private static Run RunOf(string id, ulong seed, DateTime started, params TargetResult[] results)
    {
        var run = new Run
        {
            Id = id,
            Setup = new Setup { Character = "Dev" },
            GameBuild = "26316041",
            Cs2Mvid = "cs2-a",
            SetupHash = "setup-a",
            Seed = seed,
            Trials = 1000,
            State = RunState.Finished,
            StartedAt = started,
        };
        foreach (var r in results)
        {
            run.Targets.Add(r.Key);
            run.Results[r.Key] = r;
        }
        return run;
    }

    private static TargetResult Result(string key, TargetState state = TargetState.Finished, int trials = 1000) =>
        new()
        {
            Key = key,
            State = state,
            TrialsDone = trials,
            Reasons = state is TargetState.Finished ? [] : [Note.Text("why")],
        };

    private static readonly DateTime Day = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    // The identity a new run with the same setup would ask for.
    private static ResultIdentity Asked(Run like, string target) => ResultIdentity.Of(like, target)!;

    [Fact]
    public void AnUnsetSeedMatchesAnySeedAndKeepsTheOrigins()
    {
        var index = new ResultIndex();
        var earlier = RunOf("run-1", 7, Day, Result(Golbin));
        index.Add(earlier);
        var found = index.Find(Asked(earlier, Golbin), seed: null);
        Assert.NotNull(found);
        Assert.Equal("run-1", found.Value.Origin.RunId);
        Assert.Equal(7UL, found.Value.Origin.Seed);
        Assert.Equal(Day, found.Value.Origin.Date);
    }

    [Fact]
    public void ASetSeedMatchesOnlyThatSeed()
    {
        var index = new ResultIndex();
        var earlier = RunOf("run-1", 7, Day, Result(Golbin));
        index.Add(earlier);
        Assert.NotNull(index.Find(Asked(earlier, Golbin), seed: 7));
        Assert.Null(index.Find(Asked(earlier, Golbin), seed: 8));
    }

    [Fact]
    public void AnyIdentityChangeMisses()
    {
        var index = new ResultIndex();
        var earlier = RunOf("run-1", 7, Day, Result(Golbin));
        index.Add(earlier);
        var asked = Asked(earlier, Golbin);
        Assert.NotNull(index.Find(asked, null));
        foreach (
            var changed in new[]
            {
                asked with
                {
                    GameBuild = "26400000",
                },
                asked with
                {
                    Cs2Mvid = "cs2-b",
                },
                asked with
                {
                    SetupHash = "setup-b",
                },
                asked with
                {
                    Loadout = "MelvorBase:Loadout2",
                },
                asked with
                {
                    IgnoreRequirements = true,
                },
                asked with
                {
                    Target = Camp,
                },
                asked with
                {
                    Trials = 2000,
                },
                asked with
                {
                    EnemySeconds = 600,
                },
                asked with
                {
                    DungeonSeconds = 900,
                },
                asked with
                {
                    StopPrecision = 0.05,
                },
            }
        )
            Assert.Null(index.Find(changed, null));
    }

    [Fact]
    public void OnlyFinishedResultsOfFinalRunsAreSources()
    {
        var index = new ResultIndex();
        var run = RunOf(
            "run-1",
            7,
            Day,
            Result(Golbin, TargetState.Cancelled, 12),
            Result(Camp, TargetState.Unfinished, 3)
        );
        index.Add(run);
        Assert.Null(index.Find(Asked(run, Golbin), null));
        Assert.Null(index.Find(Asked(run, Camp), null));

        var running = RunOf("run-2", 8, Day, Result(Golbin));
        running.State = RunState.Running;
        index.Add(running);
        Assert.Null(index.Find(Asked(running, Golbin), null));
    }

    [Fact]
    public void TheNewestWinsAndAReusedResultKeepsItsFirstOrigin()
    {
        var index = new ResultIndex();
        var first = RunOf("run-1", 7, Day, Result(Golbin, trials: 1000));
        var second = RunOf("run-2", 9, Day.AddHours(1), Result(Golbin, trials: 1000));
        index.Add(second);
        index.Add(first);
        Assert.Equal("run-2", index.Find(Asked(first, Golbin), null)!.Value.Origin.RunId);

        var reused = Result(Golbin);
        reused.ReusedFrom = new ReuseOrigin
        {
            RunId = "run-2",
            Seed = 9,
            Date = Day.AddHours(1),
        };
        var third = RunOf("run-3", 11, Day.AddHours(2), reused);
        index.Add(third);
        var found = index.Find(Asked(third, Golbin), null)!.Value;
        Assert.Equal("run-2", found.Origin.RunId);
        Assert.Equal(9UL, found.Origin.Seed);
        // The seed that matters for a set seed is the one the result was simulated with.
        Assert.NotNull(index.Find(Asked(third, Golbin), seed: 9));
        Assert.Null(index.Find(Asked(third, Golbin), seed: 11));
    }

    [Fact]
    public void RemovingARunRemovesItsEntries()
    {
        var index = new ResultIndex();
        var run = RunOf("run-1", 7, Day, Result(Golbin));
        index.Add(run);
        index.Keep(["run-other"]);
        Assert.Null(index.Find(Asked(run, Golbin), null));
    }

    private const string Plant = "MelvorBase:GolbinForest/MelvorBase:EvilPlant";
    private const string Hornet = "MelvorBase:GreenMangroves/MelvorBase:GiantHornet";

    private static TargetResult Timed(string key, double seconds, TargetState state = TargetState.Finished)
    {
        var result = Result(key, state);
        result.RealSeconds = seconds;
        return result;
    }

    [Fact]
    public void TheSlowestStartFirstAndUnknownTargetsBeforeThem()
    {
        var index = new ResultIndex();
        index.Add(RunOf("run-1", 7, Day, Timed(Golbin, 0.4), Timed(Plant, 1.2), Timed(Hornet, 0.4)));
        // Unknown first in the order given, then slowest first, ties in the order given.
        Assert.Equal([Camp, Plant, Hornet, Golbin], index.LongestFirst([Hornet, Golbin, Camp, Plant]));
    }

    [Fact]
    public void TheNewestRunGivesTheDurationAndAnUnfinishedTargetCounts()
    {
        var index = new ResultIndex();
        index.Add(RunOf("run-1", 7, Day, Timed(Golbin, 2.0), Timed(Plant, 1.0)));
        var newer = RunOf("run-2", 8, Day.AddHours(1), Timed(Golbin, 0.5), Timed(Camp, 3.0, TargetState.Unfinished));
        newer.SetupHash = "setup-b"; // another setup: only the order depends on it
        index.Add(newer);
        Assert.Equal([Camp, Plant, Golbin], index.LongestFirst([Golbin, Plant, Camp]));
    }

    [Fact]
    public void ReusedAndRefusedResultsGiveNoDuration()
    {
        var index = new ResultIndex();
        var reused = Timed(Golbin, 5.0);
        reused.ReusedFrom = new ReuseOrigin
        {
            RunId = "run-0",
            Seed = 1,
            Date = Day,
        };
        index.Add(RunOf("run-1", 7, Day, reused, Timed(Camp, 4.0, TargetState.Refused), Timed(Plant, 1.0)));
        // Golbin and Camp count as never simulated: first, in the order given.
        Assert.Equal([Golbin, Camp, Plant], index.LongestFirst([Plant, Golbin, Camp]));
        index.Keep([]);
        Assert.Equal([Plant, Golbin, Camp], index.LongestFirst([Plant, Golbin, Camp]));
    }

    [Fact]
    public void RunsWithoutASetupHashAreNotIndexed()
    {
        var index = new ResultIndex();
        var old = RunOf("run-1", 7, Day, Result(Golbin));
        old.SetupHash = null;
        index.Add(old);
        Assert.Null(ResultIdentity.Of(old, Golbin));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void ASavedRunsResultsAreReadFromItsFileOncePerLookup()
    {
        var run = RunOf("run-1", 7, Day, Result(Golbin), Result(Camp));
        var reads = 0;
        var index = new ResultIndex(id =>
        {
            reads++;
            return id == "run-1" ? run : null;
        });
        index.Add(run, saved: true);
        Assert.Equal(0, reads); // nothing is read until a run asks

        var opened = new Dictionary<string, Run?>();
        Assert.Same(run.Results[Golbin], index.Find(Asked(run, Golbin), null, opened)!.Value.Result);
        Assert.Same(run.Results[Camp], index.Find(Asked(run, Camp), null, opened)!.Value.Result);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void ARunIsHeldUntilItsFileIsWritten()
    {
        var run = RunOf("run-1", 7, Day, Result(Golbin));
        Run? onDisk = null;
        var index = new ResultIndex(_ => onDisk);
        index.Add(run);
        Assert.Same(run.Results[Golbin], index.Find(Asked(run, Golbin), null)!.Value.Result);

        onDisk = RunOf("run-1", 7, Day, Result(Golbin));
        index.Saved("run-1");
        Assert.Same(onDisk.Results[Golbin], index.Find(Asked(run, Golbin), null)!.Value.Result);
    }

    [Fact]
    public void ARunThatCantBeReadAnyMoreAnswersNothingAndAnOlderOneDoes()
    {
        var older = RunOf("run-1", 7, Day, Result(Golbin));
        var newer = RunOf("run-2", 8, Day.AddHours(1), Result(Golbin));
        var index = new ResultIndex(id => id == "run-1" ? older : null);
        index.Add(older, saved: true);
        index.Add(newer, saved: true);
        Assert.Equal("run-1", index.Find(Asked(older, Golbin), null)!.Value.Origin.RunId);

        index.Clear();
        Assert.Null(index.Find(Asked(older, Golbin), null));
    }

    private static TargetResult Counted(string key)
    {
        var result = Result(key);
        result.Batches.Add(
            new Tally
            {
                Trials = 1,
                Kills = 1,
                Fight = new FightCounts(),
            }
        );
        return result;
    }

    [Fact]
    public void WhileCountingAResultWithoutCountsIsPassedOver()
    {
        var index = new ResultIndex { CountsFights = () => true };
        var old = RunOf("run-1", 7, Day, Result(Golbin));
        index.Add(old);
        Assert.Null(index.Find(Asked(old, Golbin), seed: 7));
    }

    [Fact]
    public void WithoutCountingAResultWithoutCountsAnswers()
    {
        var index = new ResultIndex { CountsFights = () => false };
        var old = RunOf("run-1", 7, Day, Result(Golbin));
        index.Add(old);
        Assert.NotNull(index.Find(Asked(old, Golbin), seed: 7));
    }

    [Fact]
    public void WhileCountingTheNewestResultWithCountsAnswersAndSeedsStillMatch()
    {
        var index = new ResultIndex { CountsFights = () => true };
        var counted = RunOf("run-1", 7, Day, Counted(Golbin));
        index.Add(counted);
        index.Add(RunOf("run-2", 7, Day.AddHours(1), Result(Golbin)));
        var found = index.Find(Asked(counted, Golbin), seed: 7);
        Assert.Equal("run-1", found!.Value.Origin.RunId);
        Assert.Null(index.Find(Asked(counted, Golbin), seed: 8));
    }

    [Fact]
    public void AMasterIsFoundWholeByItsKeyAndItsMonstersNeverAnswerTheirOwnRows()
    {
        const string master = "master:MelvorBase:Kuren";
        var part = MasterFiguresTests.Part(Ids.PartKey("Kuren", Golbin), 10, 5);
        foreach (var batch in part.Batches)
            batch.Fight = FightCountsTests.Sample();
        var kuren = new TargetResult
        {
            Key = master,
            Kind = TargetKind.Master,
            State = TargetState.Finished,
            Parts = [part],
        };
        var index = new ResultIndex { CountsFights = () => true };
        var earlier = RunOf("run-1", 7, Day, kuren);
        index.Add(earlier);
        Assert.Same(kuren, index.Find(Asked(earlier, master), null)!.Value.Result);
        Assert.Null(index.Find(Asked(earlier, Golbin), null));
        Assert.Null(index.Find(Asked(earlier, part.Key), null));
    }
}
