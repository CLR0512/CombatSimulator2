using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>A preset put on a side, from the Compare picker or from a side's Change list. Main thread.</summary>
internal static class PresetSides
{
    /// <summary>
    /// The side keeps what it starts from and takes the preset's changes in place of its own; a side B that isn't
    /// there, or is a saved run, starts as side A does. False when the preset can't be read.
    /// </summary>
    public static bool Take(SideLetter letter, string name)
    {
        if (PanelState.Presets().Find(name)?.Read(out _) is not { } changes)
            return false;
        var side =
            letter == SideLetter.A ? PanelState.A
            : PanelState.B is { CanChange: true } b ? b
            : new SideState(PanelState.A.Base, null, PanelState.A.IgnoreRequirements);
        PanelState.SetSide(letter, side.WithChanges(changes));
        return true;
    }

    /// <summary>Side B becomes side A's base with the preset's changes.</summary>
    public static void UseAsSideB(string name)
    {
        if (PanelState.Presets().Find(name)?.Read(out _) is not { } changes)
            return;
        PanelState.PickBaseline(false);
        PanelState.SetSide(SideLetter.B, new SideState(PanelState.A.Base, changes, PanelState.A.IgnoreRequirements));
    }
}
