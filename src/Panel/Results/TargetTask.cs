using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// What a target's details follow besides its run, and a single monster's slayer task under its name: whose task it
/// was fought on, or that no master gives it. The task itself is chosen per side, on the Setup tab's Slayer page.
/// </summary>
internal static class TargetTask
{
    /// <summary>A master's block toggles, loadouts and Best loadouts follow the sides' changes; null for other targets.</summary>
    public static string? Follows(TargetResult result) =>
        result.Kind is TargetKind.Master
            ? $"{PanelState.A.Changes.ShortForm}|{PanelState.B?.Changes.ShortForm}|{PanelState.Editing}|{Engine.RunService.Instance.BestLoadouts?.State}{Engine.RunService.Instance.BestLoadouts?.Results.Count}"
            : null;

    /// <summary>What to add to a monster's subtitle for the task it was fought on; empty when none.</summary>
    public static string Subtitle(TargetResult result, Func<string, string> names) =>
        result.Task is { } master
            // Translators: Under a monster's name: it was fought on this Slayer master's task, by the setup's choice.
            ? " · " + Tr.Format("On {0}'s task", names(master))
        : result.TaskNote != null
            // Translators: Under a monster's name: the setup put it on a slayer task, but no master gives it.
            ? " · " + Tr.Get("No master gives it")
        : "";
}
