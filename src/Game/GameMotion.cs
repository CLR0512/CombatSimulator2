using GamesByMalcs.Melvor2.Settings;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>The game's own "interface animations" setting, which CS2's motion follows until the player picks a level.</summary>
internal static class GameMotion
{
    /// <summary>Whether the game animates its interface; true when this build has no such setting.</summary>
    public static bool Animates() =>
        !Dependencies.IsAvailable(Dependencies.Motion) || LocalSettingsManager.Settings.Interface.InterfaceAnimations;
}
