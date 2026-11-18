using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>Finds a run by id for the commands: the current run, then the saved runs.</summary>
internal static partial class RunFinder
{
    /// <summary>Reads a saved run by id, or null when there is none (set by the store).</summary>
    public static Func<string, Run?> Saved { get; set; } = _ => null;

    /// <summary>
    /// The current run when <paramref name="id"/> is null or names it, else the saved run; throws a command error
    /// when there is none.
    /// </summary>
    public static Run Find(string? id)
    {
        var current = RunService.Instance.Current;
        if (id == null)
            return current ?? throw new CommandException("no run yet");
        if (current?.Id == id)
            return current;
        return Saved(id) ?? throw new CommandException($"no run '{id}'");
    }
}
