using GamesByMalcs.Melvor2.Notifications;
using Godot;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Keeps the game's notifications (items found, level-ups, quests) from drawing over CS2's window while the panel
/// asks: they are drawn behind everything instead, so the window covers them, and come to the front again after with
/// their full time to show. They still arrive, lay out and play their sounds as the game makes them. Main thread.
/// </summary>
internal static class QuietNotifications
{
    // Behind the game's own interface, which draws at 0: hidden without being hidden, as hiding one skips its layout
    // and leaves its box the wrong size.
    private const int Behind = -1;

    private static NotificationManager? _manager;
    private static readonly Dictionary<Notification, int> _moved = [];
    private static bool _on;

    /// <summary>
    /// Keeps notifications behind while <paramref name="quiet"/>, and brings them back once it isn't;
    /// <paramref name="anywhere"/> is any node in the game's tree.
    /// </summary>
    public static void Follow(Node anywhere, bool quiet)
    {
        quiet &= Dependencies.IsAvailable(Dependencies.Quiet);
        if (quiet == _on)
            return;
        _on = quiet;
        if (quiet)
            Hush(anywhere);
        else
            Release();
    }

    /// <summary>Brings every notification back and lets go of the game's nodes, as the mod unloads.</summary>
    public static void Reset()
    {
        _on = false;
        Release();
        _manager = null;
    }

    private static void Hush(Node anywhere)
    {
        if (_manager == null || !GodotObject.IsInstanceValid(_manager))
            _manager = Find(anywhere.GetTree().Root);
        if (_manager == null)
            return;
        _manager.ChildEnteredTree += Entered;
        foreach (var child in _manager.GetChildren())
            Entered(child);
    }

    // The game keeps one manager under its main scene; found once.
    private static NotificationManager? Find(Node node)
    {
        if (node is NotificationManager manager)
            return manager;
        foreach (var child in node.GetChildren())
            if (Find(child) is { } found)
                return found;
        return null;
    }

    private static void Entered(Node node)
    {
        if (node is not Notification notification || _moved.ContainsKey(notification))
            return;
        _moved[notification] = notification.ZIndex;
        notification.ZIndex = Behind;
    }

    private static void Release()
    {
        if (_manager != null && GodotObject.IsInstanceValid(_manager))
            _manager.ChildEnteredTree -= Entered;
        foreach (var (notification, z) in _moved)
        {
            if (!GodotObject.IsInstanceValid(notification))
                continue;
            notification.ZIndex = z;
            // Seen only now: it shows for its whole time from here, as if it had just arrived. One still coming in
            // starts its time once it is in; one going is left to go.
            if (notification.State == Notification.NotificationState.Fired)
                notification.ResetAutoDismiss();
        }
        _moved.Clear();
    }
}
