using System.Security.Cryptography;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HostProtocolTests
{
    private static byte[] Frames(params (MessageKind Kind, byte[] Payload)[] frames)
    {
        using var stream = new MemoryStream();
        var writer = new FrameWriter(stream);
        foreach (var (kind, payload) in frames)
            writer.Write(kind, payload);
        return stream.ToArray();
    }

    private static T RoundTrip<T>(MessageKind kind, T message)
    {
        using var stream = new MemoryStream();
        new FrameWriter(stream).Send(kind, message);
        stream.Position = 0;
        var frame = new FrameReader(stream).Read();
        Assert.NotNull(frame);
        Assert.Equal(kind, frame.Value.Kind);
        var back = Protocol.Decode<T>(frame.Value);
        Assert.Equal(Json(message), Json(back));
        return back;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, RunFile.Options);

    // A stream that hands out at most `chunk` bytes per read, as a pipe may.
    private sealed class Trickle(byte[] bytes, int chunk) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, chunk));

        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, chunk)]);
    }

    [Fact]
    public void AFrameIsLengthKindPayloadWithLengthCountingKindAndPayload()
    {
        var bytes = Frames((MessageKind.Stop, [1, 2, 3]));

        Assert.Equal([4, 0, 0, 0, 13, 1, 2, 3], bytes);
    }

    [Fact]
    public void AFrameSplitAcrossReadsIsReassembled()
    {
        var payload = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        var bytes = Frames((MessageKind.Snapshot, payload), (MessageKind.Stop, []));
        var reader = new FrameReader(new Trickle(bytes, 3));

        var first = reader.Read();
        var second = reader.Read();

        Assert.Equal(MessageKind.Snapshot, first!.Value.Kind);
        Assert.Equal(payload, first.Value.Payload);
        Assert.Equal(MessageKind.Stop, second!.Value.Kind);
        Assert.Empty(second.Value.Payload);
        Assert.Null(reader.Read());
    }

    [Fact]
    public void ALengthOver64MbIsAProtocolError()
    {
        byte[] bytes = [.. BitConverter.GetBytes(64 * 1024 * 1024 + 2), 3];
        var error = Assert.Throws<ProtocolException>(() => new FrameReader(new MemoryStream(bytes)).Read());
        Assert.Contains("64 MB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALengthOfZeroIsAProtocolError()
    {
        var error = Assert.Throws<ProtocolException>(() => new FrameReader(new MemoryStream([0, 0, 0, 0])).Read());
        Assert.Contains("length 0", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(255)]
    public void AnUnknownKindIsAProtocolError(byte kind)
    {
        var error = Assert.Throws<ProtocolException>(() =>
            new FrameReader(new MemoryStream([1, 0, 0, 0, kind])).Read()
        );
        Assert.Contains("unknown message kind", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)] // inside the length
    [InlineData(4)] // length, no kind
    [InlineData(6)] // part of the payload
    public void EndOfStreamMidFrameIsAProtocolError(int keep)
    {
        var bytes = Frames((MessageKind.Snapshot, [1, 2, 3, 4]))[..keep];
        var error = Assert.Throws<ProtocolException>(() => new FrameReader(new MemoryStream(bytes)).Read());
        Assert.Contains("mid-frame", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EndOfStreamBetweenFramesIsAQuietEnd() => Assert.Null(new FrameReader(new MemoryStream()).Read());

    [Fact]
    public void ProgressAndTheTimingStatsRoundTrip()
    {
        var progress = RoundTrip(MessageKind.Progress, new ProgressMessage(3, "MelvorBase:HobgolbinCamp", 312));
        Assert.Equal(312, progress.TrialsDone);
        var finished = RoundTrip(
            MessageKind.Finished,
            new FinishedMessage(
                3,
                new PoolStats(
                    4,
                    4,
                    0,
                    4.4,
                    9.1,
                    CheckSeconds: 0.02,
                    Resets: 33,
                    ResetMisses: 1,
                    FightSeconds: 10.2,
                    Fought: 34,
                    Slots: 4,
                    IdleSeconds: 1.5,
                    TailSeconds: 0.25
                ),
                1,
                4
            )
        );
        Assert.Equal(33, finished.Stats.Resets);
        Assert.Equal(1, finished.Stats.ResetMisses);
        Assert.Equal(34, finished.Stats.Fought);
        Assert.Equal(4, finished.Stats.Slots);
        Assert.Equal(1.5, finished.Stats.IdleSeconds);
        Assert.Equal(0.25, finished.Stats.TailSeconds);
    }

    [Fact]
    public void HelloRoundTrips()
    {
        var hello = RoundTrip(
            MessageKind.Hello,
            new Hello(Protocol.Version, "26316041 0.1.0", "0f1e2d3c-mvid", "a1b2c3d4-mvid", 4242)
        );
        Assert.Equal(8, hello.Protocol);
        Assert.Equal(4242, hello.Pid);
        Assert.Contains("\"gameMvid\"", Json(hello), StringComparison.Ordinal);
    }

    [Fact]
    public void ADecodeOfTheWrongShapeIsAProtocolError()
    {
        var frame = new Frame(MessageKind.Hello, "not json"u8.ToArray());
        Assert.Throws<ProtocolException>(() => Protocol.Decode<Hello>(frame));
    }

    // Messages of a run.

    private static StartMessage Start(byte[] snapshot, int generation = 3) =>
        new(
            generation,
            "Dev",
            Convert.ToHexStringLower(SHA256.HashData(snapshot)),
            "26316041 0.1.0",
            "MelvorBase:Loadout2",
            ["MelvorBase:GolbinForest/MelvorBase:Golbin", "MelvorBase:HobgolbinCamp"],
            new RunOptions(1000, new TrialLimits { EnemySeconds = 120 }, true),
            18_446_744_073_709_551_615UL,
            4,
            1L << 30,
            RunId: "run-20261109-140209-1",
            Side: "B"
        );

    [Fact]
    public void StartCarriesTheRunIdAndSideForTheHostsLogLines()
    {
        var start = RoundTrip(MessageKind.Start, Start([1, 2, 3]));
        Assert.Equal("run-20261109-140209-1", start.RunId);
        Assert.Equal("B", start.Side);
        Assert.Equal(8, Protocol.Version);
    }

    [Fact]
    public void StartCarriesPartLoadoutsAndSingleTasks()
    {
        const string golbin = "master:MelvorBase:Kuren/MelvorBase:GolbinForest/MelvorBase:Golbin";
        var start = RoundTrip(
            MessageKind.Start,
            Start([1, 2, 3]) with
            {
                PartLoadouts = new() { [golbin] = "MelvorBase:Loadout3" },
                SingleTasks = new() { ["MelvorBase:SlimeForest/MelvorBase:RedSlime"] = "MelvorBase:Allevia" },
            }
        );
        Assert.Equal("MelvorBase:Loadout3", start.PartLoadouts![golbin]);
        Assert.Equal("MelvorBase:Allevia", start.SingleTasks!["MelvorBase:SlimeForest/MelvorBase:RedSlime"]);
    }

    [Fact]
    public void AStartWithoutPartLoadoutsWritesNoneOfTheirFields()
    {
        var json = System.Text.Encoding.UTF8.GetString(Protocol.Encode(Start([1, 2, 3])));
        Assert.DoesNotContain("PartLoadouts", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SingleTasks", json, StringComparison.OrdinalIgnoreCase);
        var start = RoundTrip(MessageKind.Start, Start([1, 2, 3]));
        Assert.Null(start.PartLoadouts);
        Assert.Null(start.SingleTasks);
    }

    [Fact]
    public void StartAndSnapshotRoundTripAndTheSnapshotMustMatch()
    {
        var snapshot = RandomNumberGenerator.GetBytes(300_000);
        var start = RoundTrip(MessageKind.Start, Start(snapshot));
        Assert.Equal(18_446_744_073_709_551_615UL, start.Seed);
        Assert.Equal(120, start.Options.Limits.EnemySeconds);

        Protocol.CheckSnapshot(start, snapshot);
        snapshot[10] ^= 1;
        var error = Assert.Throws<ProtocolException>(() => Protocol.CheckSnapshot(start, snapshot));
        Assert.Contains("sha256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DoneRoundTripsATargetResultFromARunFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs2-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "run" + RunFile.Extension);
            RunFile.Write(path, ResultsModelTests.SampleRun());
            var run = RunFile.Read(path);
            foreach (var result in run.Results.Values)
            {
                var back = RoundTrip(MessageKind.Done, new DoneMessage(7, result));
                Assert.Equal(Json(result), Json(back.Result));
            }
            Assert.Contains(run.Results.Values, r => r.Fights.Count > 0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GuardsRoundTripWithSkippedCountsAndBlockedStacks()
    {
        var guards = RoundTrip(
            MessageKind.Guards,
            new GuardsMessage(
                2,
                new Dictionary<string, int> { ["AudioManager.SetPlaylist"] = 40, ["AudioManager.PlayNextTrack"] = 3 },
                [new BlockedGuard("NotificationManager.AddNotification", 2, "   at A.B()\n   at C.D()")]
            )
        );
        Assert.Equal(40, guards.Skipped["AudioManager.SetPlaylist"]);
        Assert.Equal("   at A.B()\n   at C.D()", guards.Blocked[0].Stack);
    }

    [Fact]
    public void TheOtherMessagesRoundTrip()
    {
        var finished = RoundTrip(
            MessageKind.Finished,
            new FinishedMessage(4, new PoolStats(4, 12, 6, 9.5, 18.75), 2_400_000_000, 3)
        );
        Assert.Equal(new PoolStats(4, 12, 6, 9.5, 18.75), finished.Stats);
        Assert.Equal(3, finished.MinCopies);
        RoundTrip(MessageKind.State, new StateMessage(4, "MelvorBase:HobgolbinCamp", TargetState.Building));
        RoundTrip(MessageKind.MainSkill, new MainSkillMessage(4, "MelvorBase:Ranged"));
        RoundTrip(MessageKind.MainSkill, new MainSkillMessage(4, null));
        RoundTrip(MessageKind.Cancel, new CancelMessage(4));
        RoundTrip(MessageKind.Failed, new FailedMessage(4, "the copy could not be built"));
        var beat = RoundTrip(
            MessageKind.Heartbeat,
            new HeartbeatMessage(900_000_000, 2_000_000_000, 8_000_000_000, 4, 4, 37.5)
        );
        Assert.Equal(37.5, beat.CpuPercent);
        // A heartbeat without the CPU field (an older host) still reads, as 0.
        var old = Protocol.Decode<HeartbeatMessage>(
            new Frame(
                MessageKind.Heartbeat,
                """{"privateBytes":1,"peakPrivateBytes":2,"availableBytes":3,"copies":4,"generation":5}"""u8.ToArray()
            )
        );
        Assert.Equal(0, old.CpuPercent);
        Assert.Equal((0, 0, 0), (old.HeapBytes, old.HeapCommittedBytes, old.RunPeakPrivateBytes));
        var full = RoundTrip(
            MessageKind.Heartbeat,
            new HeartbeatMessage(9, 9, 9, 4, 4, HeapBytes: 300, HeapCommittedBytes: 500, RunPeakPrivateBytes: 800)
        );
        Assert.Equal((300, 500, 800), (full.HeapBytes, full.HeapCommittedBytes, full.RunPeakPrivateBytes));
        RoundTrip(MessageKind.Dev, new DevMessage("hang"));
        RoundTrip(MessageKind.Stop, new StopMessage());
    }

    [Fact]
    public void GenerationFilterKeepsOnlyTheCurrentRun()
    {
        var filter = new GenerationFilter();
        Assert.False(filter.Accept(0));
        filter.Current = 5;
        Assert.True(filter.Accept(5));
        Assert.False(filter.Accept(4));
        Assert.False(filter.Accept(6));
    }

    [Fact]
    public void AStartRightAfterCancelIsAcceptedAndTheCancelledOutputIsDropped()
    {
        var gate = new RunGate();
        var filter = new GenerationFilter();

        gate.Start(1);
        filter.Current = 1;
        gate.Cancel(1);
        gate.Start(2); // accepted while generation 1's builds are still finishing
        filter.Current = 2;

        Assert.Equal(2, gate.Active);
        Assert.False(filter.Accept(1)); // late output of the cancelled run
        Assert.True(filter.Accept(2));
    }

    [Fact]
    public void AStartDuringAnUncancelledRunIsAProtocolError()
    {
        var gate = new RunGate();
        gate.Start(1);
        Assert.Throws<ProtocolException>(() => gate.Start(2));
        gate.End(1);
        gate.Start(2);
        Assert.Equal(2, gate.Active);
    }

    [Fact]
    public void ACancelOrEndOfAnotherGenerationLeavesTheActiveRun()
    {
        var gate = new RunGate();
        gate.Start(2);
        gate.Cancel(1);
        gate.End(1);
        Assert.Equal(2, gate.Active);
    }

    [Fact]
    public void ADevMessageWithNoDevPartIsLoggedOnceAndServingGoesOn()
    {
        // A Release host has no dev part: each dev frame comes to the fallback, which says so once.
        var fallback = new DevFrameFallback();
        var logged = new List<string>();
        fallback.NotHandled(logged.Add);
        fallback.NotHandled(logged.Add);
        Assert.Equal(["dev message ignored"], logged);
        // The frame itself decodes as any other, so the read loop goes on to the next message.
        using var stream = new MemoryStream();
        var writer = new FrameWriter(stream);
        writer.Send(MessageKind.Dev, new DevMessage("hang"));
        writer.Send(MessageKind.Stop, new StopMessage());
        stream.Position = 0;
        var reader = new FrameReader(stream);
        Assert.Equal(MessageKind.Dev, reader.Read()!.Value.Kind);
        Assert.Equal(MessageKind.Stop, reader.Read()!.Value.Kind);
    }
}
