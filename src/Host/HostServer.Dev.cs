using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Dev;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Host;

internal sealed partial class HostServer
{
    /// <summary>Makes the host report another CS2 build in its hello, to test the mismatch refusal.</summary>
    public const string FakeMvidVariable = "MELVORMODS_CS2_FAKE_MVID";

    private volatile bool _hung;
    private volatile bool _collectAsked;

    static partial void HelloCs2(ref string cs2)
    {
        if (Environment.GetEnvironmentVariable(FakeMvidVariable) is { Length: > 0 } fake)
            cs2 = fake;
    }

    partial void DevFrame(Frame frame, ref bool handled)
    {
        var action = Protocol.Decode<DevMessage>(frame).Action;
        if (action.StartsWith("throw ", StringComparison.Ordinal) && action is not ("throw build" or "throw thread"))
        {
            var key = action["throw ".Length..];
            Log.Warn($"dev: the next fight of {key} throws");
            PoolFaults.ThrowOn(key);
            handled = true;
            return;
        }
        switch (action)
        {
            case "hang":
                Log.Warn("dev: hanging (no more heartbeats or work)");
                _hung = true;
                _cancel?.Cancel();
                Thread.Sleep(Timeout.Infinite);
                handled = true;
                break;
            case "collect":
                _collectAsked = true;
                handled = true;
                break;
            case "crash":
                Log.Warn("dev: crashing through Godot's own fatal error");
                Godot.OS.Crash("cs2dev host crash");
                handled = true;
                break;
            case "throw":
                Log.Warn("dev: the next target to fight throws");
                PoolFaults.ThrowNextFight();
                handled = true;
                break;
            case "throw build":
                Log.Warn("dev: the next copy prepared throws");
                PoolFaults.ThrowNextBuild();
                handled = true;
                break;
            case "throw thread":
                Log.Warn("dev: throwing on a thread nothing catches");
                new Thread(() => throw new InvalidOperationException("cs2dev throw thread"))
                {
                    Name = "cs2-dev-throw",
                }.Start();
                handled = true;
                break;
            case "overflow":
                Log.Warn("dev: overflowing the stack");
                handled = Overflow(0) > 0;
                break;
            case "av":
                Log.Warn("dev: writing to an unmapped address");
                System.Runtime.InteropServices.Marshal.WriteInt64(new IntPtr(0x7FF0_0000_0000), 1);
                handled = true;
                break;
            case "failfast":
                Log.Warn("dev: failing fast");
                Environment.FailFast("cs2dev failfast");
                break;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Overflow(int depth) => Overflow(depth + 1) + 1;

    partial void Hung(ref bool hung) => hung = _hung;

    // One full compacting collection, as between runs, with what the host held either side: what is left is live.
    partial void BeforeBeat(ref bool beatNow)
    {
        if (!_collectAsked)
            return;
        _collectAsked = false;
        var state =
            _gate.Active != null ? $"fighting {_pool?.Fighting ?? 0}"
            : _stock.Count > 0 ? "stock"
            : "empty";
        var before = (Private: ProcessMeter.PrivateBytes(), Heap: ProcessMeter.Heap());
        CollectEverything();
        var after = (Private: ProcessMeter.PrivateBytes(), Heap: ProcessMeter.Heap());
        static long Mb(long bytes) => bytes / 1048576;
        Log.Info(
            $"dev: collected in {state}: private {Mb(before.Private)} → {Mb(after.Private)} MB, heap {Mb(before.Heap.InUse)} → {Mb(after.Heap.InUse)} MB, committed {Mb(before.Heap.Committed)} → {Mb(after.Heap.Committed)} MB"
        );
        beatNow = true;
    }
}
