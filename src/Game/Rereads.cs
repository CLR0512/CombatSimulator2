using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Loadouts;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Components.Prayers;
using GamesByMalcs.Melvor2.Components.SlayerTasks;
using GamesByMalcs.Melvor2.Components.Toolbelt;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// What a component must have emptied before it reads its saved state a second time. The game's readers are written
/// for a component just made: several add what they read to what the component holds instead of replacing it, so a
/// second read leaves it holding everything twice, or fails on the first loadout it already has.
/// </summary>
internal static class Rereads
{
    public static void Empty(object component)
    {
        switch (component)
        {
            case PlayerComponent player:
                player.DamagedByStances.Clear();
                player._selectedStances.Clear();
                player.LoadoutStances.Clear();
                break;
            case CharacterComponent character:
                character.DamagedByStances.Clear();
                break;
            case EquipmentComponent equipment:
                equipment.Loadouts.Clear();
                break;
            case PrayerComponent prayers:
                prayers.Loadouts.Clear();
                prayers.FavouritePrayers.Clear();
                prayers.HiddenPrayers.Clear();
                break;
            case ToolbeltComponent toolbelt:
                toolbelt.Loadouts.Clear();
                break;
            case MagicSpellsComponent spells:
                spells.Loadouts.Clear();
                spells._defaultSpellsForElement.Clear();
                break;
            case FoodComponent food:
                food.Loadouts.Clear();
                break;
            case LoadoutComponent loadouts:
                loadouts._loadoutDisplayOrder.Clear();
                break;
            case SlayerTasksComponent slayer:
                // A master's reader leaves the task it holds when the save has none.
                foreach (var master in slayer.ActiveMasters.Values)
                    master._currentTask = null;
                slayer._mastersWithTask.Clear();
                break;
        }
    }

    /// <summary>
    /// What a component works out from its saved state once, when a save loads, and keeps up to date from events
    /// after that. A second read raises no event, so it is worked out again here.
    /// </summary>
    public static void Derive(object component)
    {
        if (component is not SlayerTasksComponent slayer)
            return;
        TasksByEnemy(slayer);
        // A master's pool is built once when a save loads, and a read puts back blocks without it: a copy whose blocks
        // a change moved would otherwise draw tasks from the changed pool.
        foreach (var master in slayer.ActiveMasters.Values)
            Slayer.SlayerTasks.RebuildPool(master);
    }

    /// <summary>
    /// The enemies that have a slayer task. A fight that finishes a task is given the next one; left as the fight had
    /// it, the copy would go on treating that next enemy as a task, and gear that is stronger against tasks would hit
    /// harder in a later fight against it.
    /// </summary>
    public static void TasksByEnemy(SlayerTasksComponent slayer)
    {
        foreach (var master in slayer.ActiveMasters.Values)
        {
            if (master.CurrentTask is not { } enemy)
                continue;
            if (!slayer._mastersWithTask.TryGetValue(enemy, out var masters))
                slayer._mastersWithTask[enemy] = masters = [];
            masters.Add(master);
        }
    }
}
