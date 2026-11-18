using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ReplayMatchTests
{
    [Fact]
    public void AFailureMatchesOnKindPhaseAndTarget()
    {
        var expected = new FailureFacts("target", "fight", "area:1/enemy:2");

        Assert.True(ReplayMatch.Matches(expected, new FailureFacts("target", "fight", "area:1/enemy:2")));
        Assert.False(ReplayMatch.Matches(expected, new FailureFacts("target", "fight", "area:1/enemy:3")));
        Assert.False(ReplayMatch.Matches(expected, new FailureFacts("target", "build", "area:1/enemy:2")));
        Assert.False(ReplayMatch.Matches(expected, new FailureFacts("run", "fight", "area:1/enemy:2")));
    }

    [Fact]
    public void WhatTheReportDidntRecordMatchesAnything()
    {
        var expected = new FailureFacts("host-hang");

        Assert.True(ReplayMatch.Matches(expected, new FailureFacts("host-hang", "fight", "area:1/enemy:2")));
        Assert.True(ReplayMatch.Matches(expected, new FailureFacts("host-hang")));
    }

    [Fact]
    public void AHostCrashMatchesWhateverItsExitCodeAndShowsIt()
    {
        var outcome = ReplayMatch.Failure(
            "run-1",
            null,
            "host",
            new FailureFacts("host-crash", ExitCode: -1073740791),
            [new FailureFacts("host-crash", ExitCode: -2146232797)],
            []
        );

        Assert.True(outcome.Matched);
        Assert.Equal("replay run-1 in host: matched, failure host-crash exit -2146232797", outcome.Line());
    }

    [Fact]
    public void ADroppedCopyMatchesADroppedCopyInTheBuildPhase()
    {
        var expected = new FailureFacts("copy", "build");

        Assert.True(
            ReplayMatch.Failure("run-1", null, "host", expected, [new FailureFacts("copy", "build")], []).Matched
        );
        var unmatched = ReplayMatch.Failure("run-1", null, "host", expected, [], []);
        Assert.False(unmatched.Matched);
        Assert.Equal(
            "replay run-1 in host: not matched, no failure (report failure copy phase build)",
            unmatched.Line()
        );
    }

    [Fact]
    public void AnyOfTheRerunsFailuresCanMatch()
    {
        var outcome = ReplayMatch.Failure(
            "run-1",
            null,
            "host",
            new FailureFacts("target", "fight", "t:b"),
            [new FailureFacts("target", "fight", "t:a"), new FailureFacts("target", "fight", "t:b")],
            []
        );

        Assert.True(outcome.Matched);
        Assert.Equal("failure target phase fight target t:b", outcome.Got);
    }

    [Fact]
    public void AFinishedRunMatchesOnItsHash()
    {
        Assert.True(ReplayMatch.Hash("run-1", 'A', "host", "3f9c", "3f9c", null, []).Matched);
        var other = ReplayMatch.Hash("run-1", 'B', "host", "3f9c", "51ab", null, []);
        Assert.False(other.Matched);
        Assert.Equal("replay run-1 B in host: not matched, hash 51ab (report hash 3f9c)", other.Line());
        var failed = ReplayMatch.Hash("run-1", null, "game", "3f9c", null, new FailureFacts("run"), []);
        Assert.False(failed.Matched);
        Assert.Equal("failure run", failed.Got);
    }

    [Fact]
    public void ASearchMatchesOnBestTiesAndStages()
    {
        var expected = new SearchOutcome
        {
            State = "Found",
            Best = "gear=Ring:a",
            Ties = ["gear=Ring:b"],
            Stages = ["200/41/6"],
        };
        var same = new SearchOutcome
        {
            State = "Found",
            Best = "gear=Ring:a",
            Ties = ["gear=Ring:b"],
            Stages = ["200/41/6"],
        };
        var otherStage = new SearchOutcome
        {
            State = "Found",
            Best = "gear=Ring:a",
            Ties = ["gear=Ring:b"],
            Stages = ["200/40/7"],
        };

        var matched = ReplayMatch.Search("search-1", "host", expected, same, []);
        Assert.True(matched.Matched);
        Assert.Equal(
            "replay search-1 in host: matched, found, best set gear=Ring:a, ties gear=Ring:b, stages 200/41/6",
            matched.Line()
        );
        Assert.False(ReplayMatch.Search("search-1", "host", expected, otherStage, []).Matched);
        Assert.False(ReplayMatch.Search("search-1", "host", expected, null, []).Matched);
    }

    [Fact]
    public void AFailedSearchMatchesAtTheSameStage()
    {
        var expected = new SearchFailure(2, "target", "t:a");

        Assert.True(
            ReplayMatch.SearchFailure("s", "host", expected, new SearchFailure(2, "target", "t:a"), []).Matched
        );
        Assert.False(
            ReplayMatch.SearchFailure("s", "host", expected, new SearchFailure(1, "target", "t:a"), []).Matched
        );
    }
}
