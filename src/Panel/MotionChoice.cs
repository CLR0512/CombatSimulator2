using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// How much the panel animates: the level the player picked, and until they pick one, everything or nothing as the
/// game's own interface animations are on or off.
/// </summary>
internal static class MotionChoice
{
    public static MotionLevel Level =>
        PanelState.Prefs.Motion ?? (GameMotion.Animates() ? MotionLevel.Full : MotionLevel.Off);

    /// <summary>Puts the level in use; the game's setting may have changed while the window was closed.</summary>
    public static void Follow() => Motion.SetLevel(Level);

    public static void Set(MotionLevel level)
    {
        PanelState.Prefs.Motion = level;
        PanelState.Save();
        Motion.SetLevel(level);
    }
}
