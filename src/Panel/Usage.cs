using System.Diagnostics;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Help;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// Keeps <see cref="UsageStats"/> for the About window: time while CS2's window is open and the game has focus, the
/// runs and comparisons as they end, and whether this version's changes are still unseen. Saved when the window
/// closes, when the game exits and at most once a minute between. Main thread.
/// </summary>
internal static class Usage
{
    private const double SaveEverySeconds = 60;

    // A longer frame is the game stopped (a suspended machine, a debugger), not time spent with CS2.
    private const double LongestFrameSeconds = 1;

    private static string? _folder;
    private static long _lastFrame;
    private static long _lastSave;

    public static UsageStats Stats { get; private set; } = new();

    /// <summary>CS2's manifest: its version and the game builds it was checked with.</summary>
    public static Manifest? Manifest { get; private set; }

    public static string Version => Manifest?.Version ?? RunService.Cs2Version;

    /// <summary>
    /// Whether the About button shows its dot: this version has changes listed, the player hasn't opened About since
    /// it was installed, and update notifications are on.
    /// </summary>
    public static bool Dot =>
        PanelState.Prefs.UpdateNotifications && Stats.Unseen(Version) && VersionNews.For(Version).Count > 0;

    internal static void Start(ModContext context)
    {
        Manifest = context.Manifest;
        _folder = context.DataRoot;
        var (stats, problem) = UsageStats.Read(_folder);
        if (problem != null)
            Log.Warn($"time with CS2 not read, starting again: {problem}");
        Stats = stats;
        Stats.StartOn(Version);
        RunService.Instance.Finalised += Stats.Count;
        RunService.Instance.Compared += Stats.Count;
        Cs2Modal.OpenChanged += WindowOpened;
        _lastFrame = _lastSave = Stopwatch.GetTimestamp();
        context.OnFrame(OnFrame);
        context.OnExit(Save);
    }

    /// <summary>Notes that the player has seen this version's changes, which takes the dot off the About button.</summary>
    public static void See()
    {
        if (!Stats.Unseen(Version))
            return;
        Stats.See(Version);
        Save();
        AboutButton.Show();
    }

    public static void SetUpdateNotifications(bool on)
    {
        PanelState.Prefs.UpdateNotifications = on;
        PanelState.Save();
        AboutButton.Show();
    }

    private static void WindowOpened(bool open)
    {
        if (!open)
        {
            Save();
            return;
        }
        var first = Stats.FirstOpened == null;
        Stats.Opened(DateTime.UtcNow);
        // A new player meets CS2 through About: what it is, the guide and tour, and Spoiler guard. Once the window is
        // built, so About opens over it.
        if (first)
            MainThread.Post(() =>
            {
                if (Cs2Modal.IsOpen)
                    HelpLayer.OpenAbout(null);
            });
    }

    private static void OnFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var frame = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        if (Cs2Modal.IsOpen && Watched())
            Stats.AddTime(Math.Min(frame, LongestFrameSeconds));
        if (Stats.Unsaved && Stopwatch.GetElapsedTime(_lastSave, now).TotalSeconds >= SaveEverySeconds)
            Save();
    }

    // A minimised window can still be the one Windows calls active, so focus alone doesn't say it is looked at.
    private static bool Watched() =>
        DisplayServer.WindowIsFocused() && DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Minimized;

    private static void Save()
    {
        _lastSave = Stopwatch.GetTimestamp();
        if (_folder == null || !Stats.Unsaved)
            return;
        try
        {
            Stats.Write(_folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("time with CS2 not saved", e);
        }
    }

    public static void Reset()
    {
        Save();
        _folder = null;
        Manifest = null;
        Stats = new();
    }
}
