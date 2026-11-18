using MelvorMods.CombatSimulator2.Panel.Dev;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2;

internal sealed partial class CombatSimulator2Mod
{
    static partial void BeforeFrameCallbacks(ModContext context) => context.OnFrame(FrameAlloc.Before);

    static partial void Keeping(ref bool keep) => keep = !Commands.Dev.BudgetsBench.SavesNothing;
}
