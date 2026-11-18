using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

internal static partial class RunPlan
{
    /// <summary>
    /// The run's single monsters with the slayer task the setup chooses for each (<see cref="Change.TaskChoice"/>):
    /// on, the master <paramref name="masterFor"/> names, or none when no master gives it; off, no task. Monsters as the
    /// save has them, masters' monsters, dungeons and masters are as they were.
    /// </summary>
    public static List<TargetSpec> WithSlayerTasks(
        List<TargetSpec> targets,
        ChangeList? changes,
        Func<TargetSpec, string?> masterFor
    )
    {
        if (changes == null || !changes.Any(c => c.Kind is ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor))
            return targets;
        return
        [
            .. targets.Select(t =>
                t.Kind != TargetKind.Enemy || t.Task != null
                    ? t
                    : Change.TaskChoice(changes, t.TargetKey) switch
                    {
                        Change.On => t with { SingleTask = masterFor(t) ?? TargetSpec.NoMaster },
                        Change.Off => t with { SingleTask = TargetSpec.OffTask },
                        _ => t,
                    }
            ),
        ];
    }
}
