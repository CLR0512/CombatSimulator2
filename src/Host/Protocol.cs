using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Host;

// What the live game and the simulator host say to each other over the worker pipe. Godot-free.

/// <summary>Frame kinds. The numbers are on the wire: never renumber, only add (with a new protocol version).</summary>
internal enum MessageKind : byte
{
    Hello = 1,
    Start = 2,
    Snapshot = 3,
    Cancel = 4,
    State = 5,
    MainSkill = 6,
    Done = 7,
    Guards = 8,
    Finished = 9,
    Failed = 10,
    Heartbeat = 11,

    // Dev builds only; a Release host logs and ignores it. The number stays reserved.
    Dev = 12,
    Stop = 13,
    Progress = 14,
    Warm = 15,
    Release = 16,
    SearchStart = 17,
    CandidateDone = 18,
    SearchEnd = 19,
    SearchPlan = 20,
    SearchPlanned = 21,
    CopyDropped = 22,
}

/// <summary>Where the warm stock stands, as heartbeats report it.</summary>
internal enum WarmState
{
    /// <summary>No warm-up asked, or released.</summary>
    None,

    /// <summary>Building bare copies toward the target.</summary>
    Filling,

    /// <summary>The target reached (or as many as the memory reserve allows).</summary>
    Ready,
}

/// <summary>The other side broke the protocol; the connection can't be trusted any more.</summary>
internal sealed class ProtocolException(string message, Exception? inner = null) : Exception(message, inner);

internal readonly record struct Frame(MessageKind Kind, byte[] Payload);

internal static class Protocol
{
    /// <summary>Changes with any incompatible change to the messages.</summary>
    public const int Version = 8;

    public const int MaxLength = 64 * 1024 * 1024;

    public static byte[] Encode<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, RunFile.Options);

    public static T Decode<T>(Frame frame)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(frame.Payload, RunFile.Options)
                ?? throw new ProtocolException($"{frame.Kind}: empty message");
        }
        catch (JsonException e)
        {
            throw new ProtocolException($"{frame.Kind}: unreadable message: {e.Message}", e);
        }
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The snapshot that follows a start must be the one it names.</summary>
    public static void CheckSnapshot(StartMessage start, byte[] snapshot)
    {
        ArgumentNullException.ThrowIfNull(start);
        CheckSnapshot(start.Sha256, snapshot);
    }

    /// <summary>The snapshot must be the one <paramref name="named"/> by its sha256.</summary>
    public static void CheckSnapshot(string named, byte[] snapshot)
    {
        ArgumentNullException.ThrowIfNull(named);
        var sha = Sha256(snapshot);
        if (sha != named)
            throw new ProtocolException(
                $"snapshot sha256 {sha[..12]} does not match the start's {named[..Math.Min(12, named.Length)]}"
            );
    }
}

/// <summary>Reads frames: <c>[u32 length LE][u8 kind][payload]</c>, <c>length</c> counting kind and payload.</summary>
internal sealed class FrameReader(Stream stream)
{
    private readonly byte[] _header = new byte[4];

    /// <summary>The next frame, or null when the stream ends between frames.</summary>
    public Frame? Read()
    {
        var got = Fill(_header);
        if (got == 0)
            return null;
        if (got < _header.Length)
            throw new ProtocolException("the stream ended mid-frame (in the length)");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(_header);
        if (length == 0)
            throw new ProtocolException("frame length 0: a frame has at least a kind");
        if (length > Protocol.MaxLength)
            throw new ProtocolException($"frame length {length} is over the 64 MB limit");
        var body = new byte[length];
        if (Fill(body) < body.Length)
            throw new ProtocolException("the stream ended mid-frame");
        var kind = (MessageKind)body[0];
        if (!Enum.IsDefined(kind))
            throw new ProtocolException($"unknown message kind {body[0]}");
        return new Frame(kind, body[1..]);
    }

    private int Fill(byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}

/// <summary>Writes whole frames; safe to share between threads.</summary>
internal sealed class FrameWriter(Stream stream)
{
    private readonly Lock _lock = new();

    public void Write(MessageKind kind, ReadOnlySpan<byte> payload)
    {
        if (payload.Length + 1 > Protocol.MaxLength)
            throw new ProtocolException($"{kind}: {payload.Length} bytes is over the 64 MB limit");
        Span<byte> header = stackalloc byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(payload.Length + 1));
        header[4] = (byte)kind;
        lock (_lock)
        {
            stream.Write(header);
            stream.Write(payload);
            stream.Flush();
        }
    }

    public void Send<T>(MessageKind kind, T message) => Write(kind, Protocol.Encode(message));
}

/// <summary>The host's first frame: what it is, so the live game can refuse a mismatched one.</summary>
internal sealed record Hello(int Protocol, string Build, string GameMvid, string Cs2Mvid, int Pid);

/// <summary>How each target is fought, as the run's drive options.</summary>
internal sealed record RunOptions(
    int Trials,
    TrialLimits Limits,
    bool IgnoreRequirements,
    double? StopPrecision = null
);

/// <summary>
/// A run for the host; the snapshot's bytes follow in their own frame. <c>Targets</c> are only those to simulate:
/// targets answered from earlier runs are left out. <c>Reuse</c> lets a copy fight target after target.
/// <c>Changes</c> is the setup's changes in short form, left out when there are none. <c>RunId</c> and <c>Side</c>
/// (<c>A</c>, <c>B</c> or null) tie the host's log lines to the game's. <c>PartLoadouts</c> is the loadout each master's
/// monster given one wears, by its key; <c>SingleTasks</c> is each single monster's slayer task by its key (a master's
/// id, or "" for none); each is left out when empty.
/// </summary>
internal sealed record StartMessage(
    int Generation,
    string Character,
    string Sha256,
    string Build,
    string? LoadoutSlot,
    List<string> Targets,
    RunOptions Options,
    ulong Seed,
    int Copies,
    long ReserveBytes,
    bool Reuse = true,
    Dictionary<string, ulong>? TargetSeeds = null,
    int NextRunCopies = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? Changes = null,
    string? RunId = null,
    string? Side = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        Dictionary<string, string>? PartLoadouts = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        Dictionary<string, string>? SingleTasks = null
);

internal sealed record CancelMessage(int Generation);

internal sealed record StateMessage(int Generation, string Key, TargetState State);

internal sealed record MainSkillMessage(int Generation, string? Skill);

internal sealed record DoneMessage(int Generation, TargetResult Result);

/// <summary>A fighting target's trials done so far.</summary>
internal sealed record ProgressMessage(int Generation, string Key, int TrialsDone);

/// <summary>The guards one run hit in the host.</summary>
internal sealed record GuardsMessage(int Generation, Dictionary<string, int> Skipped, List<BlockedGuard> Blocked);

/// <summary>A run's end: <c>PeakPrivateBytes</c> is the highest the host read while this run was active.</summary>
internal sealed record FinishedMessage(int Generation, PoolStats Stats, long PeakPrivateBytes, int MinCopies);

internal sealed record FailedMessage(int Generation, string Error);

/// <summary>A copy failed to build and was dropped; the run goes on with the others.</summary>
internal sealed record CopyDroppedMessage(int Generation, string Error);

/// <summary>
/// Every second from the host: its memory, the machine's, what it is working on (0 when idle), its CPU use over the
/// last second as a percent of the whole machine (0 from a host that doesn't say), its warm stock, its managed heap
/// (in use, and held by the runtime) and the active run's own peak of private bytes (0 when idle).
/// <c>PeakPrivateBytes</c> is the process's lifetime peak.
/// </summary>
internal sealed record HeartbeatMessage(
    long PrivateBytes,
    long PeakPrivateBytes,
    long AvailableBytes,
    int Copies,
    int Generation,
    double CpuPercent = 0,
    int Stock = 0,
    int StockTarget = 0,
    WarmState Warm = WarmState.None,
    long HeapBytes = 0,
    long HeapCommittedBytes = 0,
    long RunPeakPrivateBytes = 0
);

/// <summary>
/// Build <c>StockTarget</c> bare copies ahead of the next run, within <c>ReserveBytes</c> of free memory.
/// </summary>
internal sealed record WarmMessage(int StockTarget, long ReserveBytes);

/// <summary>Drop the warm stock.</summary>
internal sealed record ReleaseMessage();

internal sealed record StopMessage();
