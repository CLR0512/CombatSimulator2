using System.Diagnostics;
using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>The live game's frames over a stretch of time, sampled once a frame on the main thread.</summary>
internal sealed class FrameWindow
{
    public readonly Stopwatch Watch = Stopwatch.StartNew();
    private readonly ulong _frames0 = Godot.Engine.GetPhysicsFrames();
    private readonly TimeSpan _gc0 = GC.GetTotalPauseDuration();
    private readonly int[] _counts0 = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
    private readonly List<double> _frameMs = new(4096);
    public readonly List<double> Fps = new(4096);
    public double Last;
    public double LagMax = double.MinValue;
    private double _lag;

    public void Sample()
    {
        var now = Watch.Elapsed.TotalMilliseconds;
        _frameMs.Add(now - Last);
        Last = now;
        Fps.Add(Godot.Engine.GetFramesPerSecond());
        _lag = now / 1000 * LiveGame.TicksPerSecond - (Godot.Engine.GetPhysicsFrames() - _frames0);
        LagMax = Math.Max(LagMax, _lag);
    }

    public WindowStats Stats()
    {
        var frames = _frameMs.Skip(1).Order().ToList(); // the first delta is from the start, not a frame
        return new WindowStats(
            Fps.Count > 0 ? Fps.Average() : 0,
            Percentile(frames, 0.50),
            Percentile(frames, 0.99),
            frames.Count > 0 ? frames[^1] : 0,
            LagMax == double.MinValue ? 0 : LagMax,
            _lag,
            (GC.GetTotalPauseDuration() - _gc0).TotalMilliseconds,
            frames.Count,
            GC.CollectionCount(0) - _counts0[0],
            GC.CollectionCount(1) - _counts0[1],
            GC.CollectionCount(2) - _counts0[2],
            frames
        );
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
}
