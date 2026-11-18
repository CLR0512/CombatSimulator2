using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class FailureLog
{
    static partial void Stamp(ref FailureRecord record)
    {
        if (RunService.Instance.TakeLever(record.RunId) is { } lever)
            record = record with { Lever = lever };
    }
}
