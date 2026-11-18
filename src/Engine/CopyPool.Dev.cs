using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class CopyPool
{
    static partial void BeforeFight(string key) => PoolFaults.BeforeFight(key);

    static partial void BeforePrepare() => PoolFaults.BeforePrepare();

    static partial void Acquired(Copy copy, string origin) => FightLog.Acquired(copy, origin);

    partial void Fought(int slot, Copy copy, TargetSpec target, ulong seed, TargetResult result) =>
        FightLog.Fought(_job, slot, copy, target.Key, seed, result);
}
