namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// What is open over CS2's window (a list, a picker, a dialog, a question), newest last, so a click outside the
/// window closes that before it closes the window.
/// </summary>
internal static class Overlays
{
    private static readonly List<(object Owner, Func<bool> IsOpen, Action Close)> _open = [];

    /// <summary>Notes that <paramref name="owner"/> opened; what closed since without saying so is dropped.</summary>
    public static void Opened(object owner, Func<bool> isOpen, Action close)
    {
        _open.RemoveAll(o => ReferenceEquals(o.Owner, owner) || !Shows(o.IsOpen));
        _open.Add((owner, isOpen, close));
    }

    /// <summary>Closes the newest still open; false when nothing is.</summary>
    public static bool CloseNewest()
    {
        while (_open.Count > 0)
        {
            var (_, isOpen, close) = _open[^1];
            _open.RemoveAt(_open.Count - 1);
            if (!Shows(isOpen))
                continue;
            close();
            return true;
        }
        return false;
    }

    /// <summary>Forgets them all: the window they were in is gone.</summary>
    public static void Clear() => _open.Clear();

    // One whose nodes were freed with its view is closed.
    private static bool Shows(Func<bool> isOpen)
    {
        try
        {
            return isOpen();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
