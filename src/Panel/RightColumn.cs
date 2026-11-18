namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// The width of the Simulate tab's right column, which the Setup tab's Effect panel takes too, so the two tabs'
/// right panels stand edge to edge. Main thread.
/// </summary>
internal static class RightColumn
{
    /// <summary>The column's width as last laid out; the details' least width before it has been.</summary>
    public static float Width { get; private set; } = Results.Details.Width;

    /// <summary>Raised when the width changes.</summary>
    public static event Action? Changed;

    public static void Report(float width)
    {
        if (width <= 0 || Math.Abs(width - Width) < 0.5f)
            return;
        Width = width;
        Changed?.Invoke();
    }

    /// <summary>Forgets the width and its listeners, as the mod unloads.</summary>
    public static void Reset()
    {
        Width = Results.Details.Width;
        Changed = null;
    }
}
