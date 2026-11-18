using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

internal static partial class RunFinder
{
    /// <summary>The current run when <paramref name="id"/> is null or names it, else the saved run, else null.</summary>
    public static Run? CurrentOrSaved(string? id) =>
        id == null || RunService.Instance.Current?.Id == id ? RunService.Instance.Current : Saved(id);
}
