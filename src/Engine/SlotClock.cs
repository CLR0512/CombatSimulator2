using System.Diagnostics;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>What a fight slot's thread can be busy with.</summary>
internal enum SlotWork
{
    Build,
    Prepare,
    Check,
    Fight,
    Reset,
}

/// <summary>
/// Where each fight slot's time went in one pool run: the work its thread timed, the targets it fought and when it
/// ran out of targets. From these come the run's idle time and its tail, which say how far the run is from all
/// slots fighting all the time. A thread names its slot once with <see cref="Enter"/>; work timed on a thread with
/// no slot (a copy built ahead) counts in the totals and in no slot. Times are <see cref="TimeSpan"/> ticks.
/// </summary>
internal sealed partial class SlotClock
{
    private static readonly int Kinds = Enum.GetValues<SlotWork>().Length;

    [ThreadStatic]
    private static int _slot; // the slot the current thread works for, from 1; 0 for none

    private readonly Func<long> _now;
    private readonly long _start;
    private readonly long[][] _work; // by slot (0: no slot), then by kind
    private readonly int[] _fought;
    private readonly long?[] _left;
    private long? _end;

    /// <summary>Starts the run's clock. <paramref name="now"/> replaces the real clock in tests.</summary>
    public SlotClock(int slots, Func<long>? now = null)
    {
        _now = now ?? (static () => Stopwatch.GetElapsedTime(0).Ticks);
        _work = new long[slots + 1][];
        for (var i = 0; i <= slots; i++)
            _work[i] = new long[Kinds];
        _fought = new int[slots + 1];
        _left = new long?[slots + 1];
        _start = _now();
    }

    public int Slots => _fought.Length - 1;

    /// <summary>The current thread works for <paramref name="slot"/> from here on.</summary>
    public static void Enter(int slot) => _slot = slot;

    /// <summary>The current thread's slot spent <paramref name="ticks"/> on <paramref name="work"/>.</summary>
    public void Add(SlotWork work, long ticks) => Interlocked.Add(ref _work[_slot][(int)work], ticks);

    /// <summary>The current thread's slot fought a target.</summary>
    public void CountFight() => Interlocked.Increment(ref _fought[_slot]);

    /// <summary>The current thread's slot has no target left to take, and its thread ends.</summary>
    public void Leave()
    {
        _left[_slot] = _now();
        _slot = 0;
    }

    /// <summary>
    /// Every thread of the run has ended: stops the clock and returns <paramref name="counts"/> with the run's
    /// times, slots and targets fought filled in.
    /// </summary>
    public PoolStats End(PoolStats counts)
    {
        _end = _now();
        Ended();
        return counts with
        {
            BuildSeconds = Seconds(SlotWork.Build),
            PrepareSeconds = Seconds(SlotWork.Prepare),
            CheckSeconds = Seconds(SlotWork.Check),
            FightSeconds = Seconds(SlotWork.Fight),
            ResetSeconds = Seconds(SlotWork.Reset),
            Fought = Fought,
            Slots = Slots,
            IdleSeconds = IdleSummed,
            TailSeconds = Tail,
        };
    }

    /// <summary>Seconds of <paramref name="work"/>, summed over the slots and the threads without one.</summary>
    public double Seconds(SlotWork work) => Seconds(_work.Sum(slot => Volatile.Read(ref slot[(int)work])));

    /// <summary>Targets fought, over all slots.</summary>
    public int Fought => _fought.Sum();

    /// <summary>
    /// Seconds a slot was neither building, preparing, checking, fighting nor resetting, between the run's start and
    /// its end, summed over slots.
    /// </summary>
    public double IdleSummed => Seconds(Enumerable.Range(1, Slots).Sum(Idle));

    /// <summary>
    /// Seconds from the second-to-last slot running out of targets to the run's end: how long the last slot worked
    /// alone. 0 with one slot.
    /// </summary>
    public double Tail
    {
        get
        {
            if (Slots < 2)
                return 0;
            var end = _end ?? _now();
            var left = _left.Skip(1).Select(at => at ?? end).Order().ToList();
            return Seconds(end - left[^2]);
        }
    }

    private long Idle(int slot) => Math.Max(0, (_end ?? _now()) - _start - _work[slot].Sum());

    private static double Seconds(long ticks) => TimeSpan.FromTicks(ticks).TotalSeconds;

    partial void Ended();
}
