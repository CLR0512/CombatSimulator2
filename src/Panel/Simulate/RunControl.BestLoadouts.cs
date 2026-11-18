using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

internal sealed partial class RunControl
{
    // Best loadouts under way, on one master or every one: the share of its master fights done, and the counter's
    // words. The last run's counter says nothing of it, and the button cancels it.
    internal static (double Share, string Text)? BestLoadoutsProgress(RunService service)
    {
        if (service.BestLoadouts is not { IsActive: true } job)
            return null;
        var names = PanelState.Names();
        var master = names(Ids.MasterOf(job.Master));
        var within = (double)job.Results.Count / Math.Max(1, job.Choices.Count);
        if (service.BestMasters is { IsActive: true, Masters.Count: > 1 } all)
            // Translators: While Find best runs on several masters: the master under way, and which of them.
            return (
                (all.Done.Count + within) / Math.Max(1, all.Masters.Count),
                Tr.Format("Find best · {0} · {1} of {2} masters", [master, all.Done.Count + 1, all.Masters.Count])
            );
        if (service.BestMasters is { IsActive: true })
            // Translators: While Find best runs on one master: its loadouts fought of all it tries.
            return (
                within,
                Tr.Format("Find best · {0} · {1} of {2} loadouts", [master, job.Results.Count, job.Choices.Count])
            );
        // Translators: Under the run button while Best loadouts runs on one master: loadouts fought of all it tries.
        return (within, Tr.Format("Best loadouts · {0} · {1} of {2}", [master, job.Results.Count, job.Choices.Count]));
    }
}
