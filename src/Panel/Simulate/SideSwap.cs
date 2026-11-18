using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// ⇅ Swap: sides A and B exchanged whole, with their checks, and the finished comparison shown with them, so the
/// results read the other way round without a new run.
/// </summary>
internal static class SideSwap
{
    /// <summary>Swaps the sides; a saved run can't be side A, so it can't be swapped.</summary>
    public static void Swap()
    {
        if (PanelState.B is not { } b)
            throw new CommandException("there is no side B to swap with");
        if (!b.CanChange)
            throw new CommandException("side B is a saved run: it can only be compared against");
        var (newA, newB) = SideState.Swap(PanelState.A, b);
        var (checkA, checkB) = (PanelState.CheckA, PanelState.CheckB);
        PanelState.SetSide(SideLetter.A, newA);
        PanelState.SetSide(SideLetter.B, newB);
        // The checks are of the sides, not the letters: each goes with its side, so the cards don't read again.
        PanelState.SetCheck(SideLetter.A, checkB);
        PanelState.SetCheck(SideLetter.B, checkA);
        // The sides last run swap too, so the results shown read as run, not "Not run yet".
        if (RunService.Instance.SwapComparison() && PanelState.Ran is { B: not null } ran)
            PanelState.Ran = (RunService.Instance.Comparison!.A.Id, ran.B, ran.A);
    }
}
