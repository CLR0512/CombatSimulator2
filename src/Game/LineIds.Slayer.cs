using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

// A master's loadout per monster and the slayer task choices in a line. A loadout is one entry per monster: the
// master, the enemy, and the slot's hash as the number (a character has few slots, so its hash names one); none is one
// entry with no enemy. A monster's own task choice is its area and enemy, numbered 0 off, 1 on, 2 as the save.
internal static partial class LineIds
{
    private static IEnumerable<LineEntry> TaskEntries(Change change)
    {
        switch (change.Kind)
        {
            case ChangeKind.Loadouts:
                var master = new LineId(IdHash.Of(change.Subject!, 2), change.Subject);
                if (change.Loadouts.Count == 0)
                    yield return new(change.Kind, master, LineId.None, 0);
                foreach (var (enemy, slot) in change.Loadouts)
                    yield return new(change.Kind, master, Item(enemy), (int)IdHash.Of(slot, 3));
                break;
            case ChangeKind.SlayerTask:
                yield return new(change.Kind, LineId.None, LineId.None, change.Value == Change.On ? 1 : 0);
                break;
            default:
                var target = TargetSpec.FromKey(change.Subject!);
                yield return new(
                    change.Kind,
                    new LineId(IdHash.Of(target.Area, 2), target.Area),
                    Item(target.Enemy),
                    change.Value switch
                    {
                        Change.Off => 0,
                        Change.On => 1,
                        _ => 2,
                    }
                );
                break;
        }
    }

    // The change an entry stands for; null for a loadout entry, gathered into its master's change (a slot this build
    // doesn't have is noted as missing).
    private static Change? TaskChange(
        Registries registries,
        LineEntry entry,
        string? subject,
        string? item,
        Dictionary<string, List<string>> loadouts,
        List<Note> missing
    )
    {
        switch (entry.Kind)
        {
            case ChangeKind.Loadouts:
                if (!loadouts.TryGetValue(subject!, out var pairs))
                    loadouts[subject!] = pairs = [];
                if (item == null)
                    return null;
                var slots = registries.LoadoutSlots().Where(s => (int)IdHash.Of(s, 3) == entry.Number).Take(2).ToList();
                if (slots.Count == 1)
                    pairs.Add(item + "@" + slots[0]);
                else
                    missing.Add(Note.Of("{0}: not in this build", Change.KindName(entry.Kind)));
                return null;
            case ChangeKind.SlayerTask:
                return new Change(entry.Kind, null, entry.Number == 1 ? Change.On : Change.Off);
            default:
                return new Change(
                    entry.Kind,
                    subject + "/" + item,
                    entry.Number switch
                    {
                        0 => Change.Off,
                        1 => Change.On,
                        _ => Change.Save,
                    }
                );
        }
    }
}
