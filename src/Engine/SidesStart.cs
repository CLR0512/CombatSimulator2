using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// What <see cref="RunService.StartSides"/> started: each side's check when it had changes, and the single run
/// or the comparison. <c>SidesSame</c>: two sides were asked for, but they came out as one setup once their
/// changes were checked, so a single run was started.
/// </summary>
internal sealed record SidesStart(SideCheck? A, SideCheck? B, Run? Run, ComparedRuns? Comparison, bool SidesSame)
{
    /// <summary>
    /// Checks <paramref name="side"/>'s changes, then goes on with the check; a side without changes (or no side)
    /// needs none and goes on at once. A check that fails or can't start comes to <paramref name="failed"/>.
    /// </summary>
    public static void CheckThen(Setup? side, Action<SideCheck?> then, Action<Exception> failed)
    {
        if (side?.Changes is not { } changes)
        {
            then(null);
            return;
        }
        try
        {
            RunService.CheckSide(
                side,
                changes,
                (check, error) =>
                {
                    if (check == null)
                        failed(error ?? new InvalidOperationException("the check failed"));
                    else
                        then(check);
                }
            );
        }
        catch (CommandException e)
        {
            failed(e);
        }
    }

    /// <summary>The side as it runs: its base with the changes its check found to apply.</summary>
    public static Setup AsChecked(Setup side, SideCheck? check) =>
        new()
        {
            Character = side.Character,
            Loadout = side.Loadout,
            LoadoutName = side.LoadoutName,
            IgnoreRequirements = side.IgnoreRequirements,
            Changes = check?.Changes.Applied,
        };

    /// <summary>
    /// Whether two sides are one setup. Both are the live character's, so the character's name is left out.
    /// </summary>
    public static bool Same(Setup a, Setup b) =>
        a.Loadout == b.Loadout && a.IgnoreRequirements == b.IgnoreRequirements && Equals(a.Changes, b.Changes);
}
