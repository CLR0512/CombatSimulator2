using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class UsageStatsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs2-usage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private void WriteFile(string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(UsageStats.PathIn(_folder), text);
    }

    private static Run RunOf(RunState state, params (int Trials, bool Reused)[] targets)
    {
        var run = new Run { State = state };
        for (var i = 0; i < targets.Length; i++)
        {
            run.Results["t" + i] = new TargetResult
            {
                Key = "t" + i,
                TrialsDone = targets[i].Trials,
                ReusedFrom = targets[i].Reused ? new ReuseOrigin() : null,
            };
        }
        return run;
    }

    private static ComparedRuns Compared(RunState a, RunState b) =>
        new(new Comparison(), new Run { State = a }, new Run { State = b }, []);

    [Fact]
    public void WithoutAFileEverythingStartsEmpty()
    {
        var (stats, problem) = UsageStats.Read(_folder);
        Assert.Null(problem);
        Assert.Equal(0, stats.AllTime.Seconds);
        Assert.Equal(0, stats.AllTime.Runs);
        Assert.Null(stats.FirstOpened);
        Assert.Null(stats.SeenVersion);
        Assert.False(stats.Unsaved);
    }

    [Fact]
    public void WhatIsSavedComesBackAndTheSessionDoesNot()
    {
        var stats = new UsageStats();
        stats.StartOn("0.1.0");
        stats.Opened(new DateTime(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc));
        stats.AddTime(90.5);
        stats.Count(RunOf(RunState.Finished, (1000, false)));
        stats.Count(Compared(RunState.Finished, RunState.Finished));
        Assert.True(stats.Unsaved);
        stats.Write(_folder);
        Assert.False(stats.Unsaved);

        var (read, problem) = UsageStats.Read(_folder);
        Assert.Null(problem);
        Assert.Equal(90.5, read.AllTime.Seconds);
        Assert.Equal(1, read.AllTime.Runs);
        Assert.Equal(1000, read.AllTime.Fights);
        Assert.Equal(1, read.AllTime.Comparisons);
        Assert.Equal(new DateTime(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc), read.FirstOpened);
        Assert.Equal("0.1.0", read.SeenVersion);
        Assert.Equal(0, read.Session.Seconds);
        Assert.Equal(0, read.Session.Runs);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("{\"allTime\": \"lots\"}")]
    public void AFileThatCannotBeReadStartsEmptyAndSaysWhy(string text)
    {
        WriteFile(text);
        var (stats, problem) = UsageStats.Read(_folder);
        Assert.NotNull(problem);
        Assert.Equal(0, stats.AllTime.Runs);
        stats.AddTime(1);
        stats.Write(_folder);
        Assert.Null(UsageStats.Read(_folder).Problem);
    }

    [Fact]
    public void AFileWithFewerFieldsKeepsTheOnesItHas()
    {
        WriteFile("{\"allTime\": {\"seconds\": 120, \"runs\": 4}}");
        var (stats, problem) = UsageStats.Read(_folder);
        Assert.Null(problem);
        Assert.Equal(120, stats.AllTime.Seconds);
        Assert.Equal(4, stats.AllTime.Runs);
        Assert.Equal(0, stats.AllTime.Fights);
        Assert.Null(stats.SeenVersion);
    }

    [Fact]
    public void AFileFromALaterVersionIsReadForWhatIsKnown()
    {
        WriteFile(
            "{\"format\": 7, \"allTime\": {\"seconds\": 5, \"fights\": 9, \"bosses\": 2}, \"seenVersion\": \"9.0.0\", \"streak\": 3}"
        );
        var (stats, problem) = UsageStats.Read(_folder);
        Assert.Null(problem);
        Assert.Equal(5, stats.AllTime.Seconds);
        Assert.Equal(9, stats.AllTime.Fights);
        Assert.Equal("9.0.0", stats.SeenVersion);
    }

    [Fact]
    public void ImpossibleNumbersAreReadAsNone()
    {
        WriteFile("{\"allTime\": {\"seconds\": -30, \"runs\": -1, \"fights\": -5, \"comparisons\": -2}}");
        var stats = UsageStats.Read(_folder).Stats;
        Assert.Equal(0, stats.AllTime.Seconds);
        Assert.Equal(0, stats.AllTime.Runs);
        Assert.Equal(0, stats.AllTime.Fights);
        Assert.Equal(0, stats.AllTime.Comparisons);
    }

    [Fact]
    public void AFinishedRunCountsOnceWithTheFightsItSimulated()
    {
        var stats = new UsageStats();
        stats.Count(RunOf(RunState.Finished, (1000, false), (400, false), (1000, true)));
        Assert.Equal(1, stats.Session.Runs);
        Assert.Equal(1400, stats.Session.Fights);
        Assert.Equal(1, stats.AllTime.Runs);
        Assert.Equal(1400, stats.AllTime.Fights);
    }

    [Theory]
    [InlineData(RunState.Cancelled)]
    [InlineData(RunState.Error)]
    public void ARunThatDidNotFinishAddsItsFightsOnly(RunState state)
    {
        var stats = new UsageStats();
        stats.Count(RunOf(state, (1000, false), (250, false), (0, false)));
        Assert.Equal(0, stats.Session.Runs);
        Assert.Equal(1250, stats.Session.Fights);
    }

    [Fact]
    public void ARunAnsweredFromEarlierRunsIsARunWithoutFights()
    {
        var stats = new UsageStats();
        stats.Count(RunOf(RunState.Finished, (1000, true), (1000, true)));
        Assert.Equal(1, stats.Session.Runs);
        Assert.Equal(0, stats.Session.Fights);
    }

    [Fact]
    public void AComparisonCountsWhenBothSidesFinished()
    {
        var stats = new UsageStats();
        stats.Count(Compared(RunState.Finished, RunState.Finished));
        stats.Count(Compared(RunState.Finished, RunState.Cancelled));
        stats.Count(Compared(RunState.Cancelled, RunState.Cancelled));
        Assert.Equal(1, stats.Session.Comparisons);
        Assert.Equal(1, stats.AllTime.Comparisons);
    }

    [Fact]
    public void AComparisonSwappedIsNotCountedAgain()
    {
        var stats = new UsageStats();
        var ran = Compared(RunState.Finished, RunState.Finished);
        stats.Count(ran);
        stats.Count(ran with { SwappedFrom = ran });
        Assert.Equal(1, stats.Session.Comparisons);
    }

    [Fact]
    public void TimeAddsToBothAndNothingAddsNothing()
    {
        var (stats, _) = UsageStats.Read(_folder);
        stats.AddTime(0);
        stats.AddTime(-4);
        stats.AddTime(double.NaN);
        Assert.False(stats.Unsaved);
        stats.AddTime(0.25);
        stats.AddTime(0.5);
        Assert.Equal(0.75, stats.Session.Seconds);
        Assert.Equal(0.75, stats.AllTime.Seconds);
        Assert.True(stats.Unsaved);
    }

    [Fact]
    public void TheFirstOpeningIsKept()
    {
        var stats = new UsageStats();
        stats.Opened(new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc));
        stats.Opened(new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc), stats.FirstOpened);
    }

    [Fact]
    public void AFirstLaunchHasNothingUnseenAndAnUpdateDoesUntilSeen()
    {
        var stats = new UsageStats();
        stats.StartOn("0.1.0");
        Assert.False(stats.Unseen("0.1.0"));
        stats.StartOn("0.2.0");
        Assert.True(stats.Unseen("0.2.0"));
        stats.See("0.2.0");
        Assert.False(stats.Unseen("0.2.0"));
    }
}
