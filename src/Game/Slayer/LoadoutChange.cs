using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game.Slayer;

/// <summary>
/// The changes the run plan applies rather than a copy: a master's loadout per monster, and the slayer task choices for
/// single monsters. A copy only checks that what they name is there (a loadout the character has and has unlocked, a
/// monster in the master's pool, a monster in an area) and leaves the copy as it was.
/// </summary>
/// <remarks>
/// A monster on its own loadout wears it through the game's swap, which replaces what these components hold and leaves
/// the rest of the side's setup: listed so <c>./mm check</c> fails when a build adds one, which changes what a loadout
/// of its own takes from the side.
/// </remarks>
[Search.ClosedSet(
    "loadout part",
    "Components.Loadouts.ILoadoutable",
    "CombatFoodComponent",
    "CombatPrayerComponent",
    "EquipmentComponent",
    "FoodComponent",
    "MagicSpellsComponent",
    "PlayerComponent",
    "PrayerComponent",
    "ToolbeltComponent"
)]
internal static class LoadoutChange
{
    public static ChangeOutcome Apply(Copy copy, Change change)
    {
        if (SlayerTasks.Master(copy.Game, change.Subject!) is not { } master)
            return Changes.Missing(change, null);
        if (change.Value == Change.None)
            return new(change, Change.None, ChangeState.NoChange, []);
        var slots = copy.Combat.Loadouts.ActiveLoadouts.Values;
        foreach (var (enemyId, slotId) in change.Loadouts)
        {
            if (!copy.Game.Data.TryGetObject<Enemy>(enemyId, out var enemy))
                return Changes.Missing(change, Change.None);
            if (!master.Data.Areas.Any(a => a.Enemies.Contains(enemy)))
                return Changes.Refused(
                    change,
                    Change.None,
                    Note.Of("{0} is not in {1}'s pool", enemy.DisplayName.Text, master.Data.DisplayName.Text)
                );
            var slot = slots.FirstOrDefault(s => s.Data.Id == slotId);
            if (slot == null)
                return Changes.Refused(change, Change.None, Note.Of("no loadout {0}", Ids.Brief(slotId)));
            if (!slot.IsUnlocked)
                return Changes.Refused(change, Change.None, Note.Of("{0} is locked", LiveGame.LoadoutName(slot)));
        }
        return new(change, Change.None, ChangeState.Accepted, []);
    }

    public static ChangeOutcome ApplyTask(Copy copy, Change change)
    {
        if (change.Kind == ChangeKind.SlayerTaskFor)
        {
            var target = TargetSpec.FromKey(change.Subject!);
            if (
                target.Enemy == null
                || !copy.Game.Data.TryGetObject<CombatArea>(target.Area, out var area)
                || !copy.Game.Data.TryGetObject<Enemy>(target.Enemy, out var enemy)
                || !area.Enemies.Contains(enemy)
            )
                return Changes.Refused(
                    change,
                    null,
                    Note.Of("{0} is not a single monster", Ids.BriefKey(change.Subject!))
                );
        }
        return new(change, null, ChangeState.Accepted, []);
    }
}
