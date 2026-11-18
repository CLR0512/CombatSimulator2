using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>Spoiler guard switched on or off: saved, and every view that names targets rebuilt.</summary>
internal static class SpoilerChoice
{
    public static void Set(bool on)
    {
        PanelState.Prefs.SpoilerGuard = on;
        PanelState.Save();
        // The target list keeps the names it was built with.
        if (SpoilerGuard.Refresh(LiveGame.Find()))
            PanelState.Catalog = null;
        PanelState.Notify();
    }
}
