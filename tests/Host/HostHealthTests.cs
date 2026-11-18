using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HostHealthTests
{
    private static readonly HostIdentity Live = new("26316041 0.1.0", "game-mvid", "cs2-mvid");

    private DateTime _now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private HostHealth Health() => new(() => _now, Live);

    private static Hello HelloFrom(HostIdentity id) => new(Protocol.Version, id.Build, id.GameMvid, id.Cs2Mvid, 99);

    private void Wait(double seconds) => _now = _now.AddSeconds(seconds);

    private HostHealth Ready()
    {
        var health = Health();
        health.Starting();
        Assert.Equal(HelloVerdict.Ok, health.OnHello(HelloFrom(Live)));
        return health;
    }

    [Fact]
    public void ARunStartsTheHostAndAGoodHelloMakesItReady()
    {
        var health = Health();
        Assert.Equal(HostState.NotStarted, health.State);
        health.Starting();
        Assert.Equal(HostState.Starting, health.State);
        Assert.Equal(_now, health.StartedAt);
        Wait(4);
        Assert.Equal(HelloVerdict.Ok, health.OnHello(HelloFrom(Live)));
        Assert.Equal(HostState.Ready, health.State);
        Assert.Null(health.Reason);
    }

    [Fact]
    public void ReadyGoesBusyAndBackForEachRunEnd()
    {
        var health = Ready();
        health.RunStarted();
        Assert.Equal(HostState.Busy, health.State);
        health.RunEnded();
        Assert.Equal(HostState.Ready, health.State);
    }

    [Fact]
    public void NoHelloWithin30SecondsFails()
    {
        var health = Health();
        health.Starting();
        Wait(29.9);
        Assert.False(health.Check());
        Wait(0.2);
        Assert.True(health.Check());
        Assert.Equal(HostState.Failed, health.State);
        Assert.Contains("30 s", health.Reason?.English(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExitingWhileStartingFails()
    {
        var health = Health();
        health.Starting();
        health.Exited(3);
        Assert.Equal(HostState.Failed, health.State);
        Assert.Contains("code 3", health.Reason?.English(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedStartKeepsItsReason()
    {
        var health = Health();
        health.Starting();
        health.StartFailed(Note.Text("the game exe was not found: verify the game files in Steam"));
        Assert.Equal(HostState.Failed, health.State);
        Assert.Equal("the game exe was not found: verify the game files in Steam", health.Reason?.English());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TenSecondsWithoutAHeartbeatFails(bool busy)
    {
        var health = Ready();
        var state = busy ? HostState.Busy : HostState.Ready;
        if (busy)
            health.RunStarted();
        Wait(9.9);
        Assert.False(health.Check());
        health.Heartbeat(); // a beat at 9.9 s keeps it alive
        Wait(9.9);
        Assert.False(health.Check());
        Assert.Equal(state, health.State);
        Wait(0.2);
        Assert.True(health.Check());
        Assert.Equal(HostState.Failed, health.State);
        Assert.Equal("the simulator stopped responding", health.Reason?.English());
        Assert.False(health.Check()); // reported once
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExitOrBrokenPipeWhileReadyOrBusyFails(bool busy)
    {
        var health = Ready();
        if (busy)
            health.RunStarted();
        health.Exited(null);
        Assert.Equal(HostState.Failed, health.State);
        Assert.Equal("the simulator closed unexpectedly", health.Reason?.English());

        var other = Ready();
        other.Exited(-1073741819);
        Assert.Equal("the simulator closed unexpectedly (code -1073741819)", other.Reason?.English());
    }

    [Fact]
    public void FailedStartsAgainOnTheNextRun()
    {
        var health = Ready();
        health.Exited(1);
        Wait(60);
        health.Starting();
        Assert.Equal(HostState.Starting, health.State);
        Assert.Null(health.Reason);
        Assert.Equal(_now, health.StartedAt);
    }

    [Fact]
    public void AFirstMismatchAsksForOneRestartAndASecondFails()
    {
        var health = Health();
        health.Starting();
        var otherCs2 = HelloFrom(Live with { Cs2Mvid = "old-cs2" });
        Assert.Equal(HelloVerdict.Restart, health.OnHello(otherCs2));
        Assert.Equal(HostState.Starting, health.State);
        health.Starting(restart: true);
        Assert.Equal(HelloVerdict.Failed, health.OnHello(otherCs2));
        Assert.Equal(HostState.Failed, health.State);
        Assert.Equal("the simulator loaded a different CS2 version; reload CS2", health.Reason?.English());
    }

    [Fact]
    public void AGameBuildMismatchNamesBothBuilds()
    {
        var health = Health();
        health.Starting();
        var other = HelloFrom(Live with { Build = "26400000 0.1.1" });
        health.OnHello(other);
        health.Starting(restart: true);
        health.OnHello(other);
        Assert.Equal(
            "the simulator loaded a different game build (26400000 0.1.1, live 26316041 0.1.0); restart the game",
            health.Reason?.English()
        );
    }

    [Fact]
    public void AProtocolMismatchIsAGameBuildStyleRefusal()
    {
        var health = Health();
        health.Starting();
        var hello = HelloFrom(Live) with { Protocol = Protocol.Version + 1 };
        Assert.Equal(HelloVerdict.Restart, health.OnHello(hello));
        health.Starting(restart: true);
        Assert.Equal(HelloVerdict.Failed, health.OnHello(hello));
        Assert.Contains("CS2 version", health.Reason?.English(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFreshStartForgetsEarlierMismatches()
    {
        var health = Health();
        health.Starting();
        health.OnHello(HelloFrom(Live with { Cs2Mvid = "x" }));
        health.Starting(restart: true);
        health.OnHello(HelloFrom(Live with { Cs2Mvid = "x" }));
        Assert.Equal(HostState.Failed, health.State);
        health.Starting();
        Assert.Equal(HelloVerdict.Restart, health.OnHello(HelloFrom(Live with { Cs2Mvid = "x" })));
    }

    [Fact]
    public void StoppingGoesBackToNotStarted()
    {
        var health = Ready();
        health.RunStarted();
        health.Stopped();
        Assert.Equal(HostState.NotStarted, health.State);
        Assert.Null(health.Reason);
        Assert.False(health.Check());
    }

    [Fact]
    public void BridgeNamesOfStates()
    {
        Assert.Equal("not-started", HostHealth.Name(HostState.NotStarted));
        Assert.Equal("starting", HostHealth.Name(HostState.Starting));
        Assert.Equal("ready", HostHealth.Name(HostState.Ready));
        Assert.Equal("busy", HostHealth.Name(HostState.Busy));
        Assert.Equal("failed", HostHealth.Name(HostState.Failed));
    }

    [Fact]
    public void HeartbeatAgeIsSinceTheLastBeat()
    {
        var health = Ready();
        Wait(1.5);
        Assert.Equal(1.5, health.HeartbeatAge!.Value.TotalSeconds, 3);
        health.Heartbeat();
        Assert.Equal(0, health.HeartbeatAge!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void AReadyHostIdlesFromItsHelloAndFromEachRunEnd()
    {
        var health = Ready();
        var hello = _now;
        Wait(299);
        Assert.Equal(hello, health.IdleSince);

        health.RunStarted();
        Wait(600); // a long run is never idle
        Assert.Null(health.IdleSince);
        health.RunEnded();
        var ended = _now;
        Wait(299);
        Assert.Equal(ended, health.IdleSince);
    }

    [Fact]
    public void OnlyAReadyHostCanBeIdle()
    {
        var health = Health();
        Assert.Null(health.IdleSince);
        health.Starting();
        Wait(10);
        Assert.Null(health.IdleSince);
        health.StartFailed(Note.Text("x"));
        Assert.Null(health.IdleSince);
    }

    private long? _dump;

    private HostHealth ReadyWithDumps()
    {
        var health = new HostHealth(() => _now, Live, (pid, _) => pid == 99 ? _dump : null);
        health.Starting();
        Assert.Equal(HelloVerdict.Ok, health.OnHello(HelloFrom(Live)));
        return health;
    }

    [Fact]
    public void AHostWhoseCrashDumpIsCompleteIsCalledCrashedThoughItStillBeats()
    {
        var health = ReadyWithDumps();
        var failures = new List<FailureKind>();
        health.Failed += (kind, _, _) => failures.Add(kind);
        _dump = 1000;
        Assert.False(health.Check());
        Wait(1);
        health.Heartbeat();
        _dump = 2000;
        Assert.False(health.Check());
        Wait(1);
        health.Heartbeat();
        Assert.False(health.Check());
        Wait(1.5);
        health.Heartbeat();

        Assert.True(health.Check());
        Assert.Equal(HostState.Failed, health.State);
        Assert.Equal([FailureKind.HostCrash], failures);
    }

    [Fact]
    public void ADumpStillGrowingHoldsOffTheHangUpToTwoMinutes()
    {
        var health = ReadyWithDumps();
        _dump = 0;
        for (var i = 0; i < 100; i++)
        {
            _dump += 1000;
            Wait(1);
            Assert.False(health.Check());
        }
        Assert.Equal(HostState.Ready, health.State);
        Wait(21);
        _dump += 1000;

        Assert.True(health.Check());
        Assert.Equal("the simulator closed unexpectedly", health.Reason!.English());
    }

    [Fact]
    public void WithoutADumpALostHeartbeatIsAHang()
    {
        var health = ReadyWithDumps();
        FailureKind? kind = null;
        health.Failed += (k, _, _) => kind = k;
        Wait(11);

        Assert.True(health.Check());
        Assert.Equal(FailureKind.HostHang, kind);
    }

    [Fact]
    public void AFailureIsAnnouncedOnceWithItsExitCode()
    {
        var health = Ready();
        var seen = new List<(FailureKind, int?)>();
        health.Failed += (kind, _, code) => seen.Add((kind, code));
        health.Exited(-1073740791);
        health.Broke(Note.Of("the simulator sent something unreadable ({0})", "x"));

        Assert.Equal([(FailureKind.HostCrash, (int?)-1073740791)], seen);
    }
}
