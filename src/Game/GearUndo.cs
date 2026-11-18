using GamesByMalcs.Melvor2.Components.Equipment;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// What a copy wore and its stance at one moment, so gear changed since can be put back through the game's own
/// equip calls. A reset alone reads the equipment back from its saved bytes, which raises none of the events the
/// character's stats follow: the copy would go on fighting with the stats of the gear it had last.
/// </summary>
internal sealed class GearUndo
{
    private readonly Copy _copy;
    private readonly Dictionary<string, string> _worn;
    private readonly string _stance;

    private GearUndo(Copy copy, Dictionary<string, string> worn, string stance)
    {
        _copy = copy;
        _worn = worn;
        _stance = stance;
    }

    /// <summary>The gear and stance of <paramref name="copy"/> as it is now.</summary>
    public static GearUndo Of(Copy copy)
    {
        using var scope = Guards.Enter();
        return new(copy, Worn(copy), copy.Combat.Player.Stats.AttackStance.Value.Id);
    }

    /// <summary>
    /// Puts the gear and stance back: every slot that differs is emptied first, so an item that moved between slots
    /// is free to go back, then filled. Returns the changes the game refused; none when the gear is as it was.
    /// </summary>
    public IReadOnlyList<ChangeOutcome> Restore()
    {
        var differing = Worn(_copy).Where(slot => slot.Value != _worn[slot.Key]).Select(slot => slot.Key).ToList();
        if (differing.Count > 0)
        {
            Changes.Apply(
                _copy,
                ChangeList.Of(differing.Select(slot => new Change(ChangeKind.Equip, slot, Change.None))),
                ignoreRequirements: true,
                throwOnRefusal: false
            );
        }
        var back = differing
            .Where(slot => _worn[slot] != Change.None)
            .Select(slot => new Change(ChangeKind.Equip, slot, _worn[slot]))
            .Append(new Change(ChangeKind.Stance, null, _stance));
        return
        [
            .. Changes
                .Apply(_copy, ChangeList.Of(back), ignoreRequirements: true, throwOnRefusal: false)
                .Where(outcome => outcome.LeftOut),
        ];
    }

    /// <summary>
    /// Moves the copy from the gear of <paramref name="previous"/> to that of <paramref name="changes"/>, each as
    /// changes on what was worn, through the copy's own change: the second set's equips that differ from what is
    /// worn, the slots only the first set changed put back, and the stance. Sets that share a weapon so never take
    /// it off and put it back. Both must hold the weapon the copy wears now. False when the game refused a change, or
    /// when either holds more than equipment and stance (then nothing is done).
    /// </summary>
    public bool Toward(ChangeList previous, ChangeList changes, bool ignoreRequirements)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(changes);
        if (!previous.Concat(changes).All(c => c.Kind is ChangeKind.Equip or ChangeKind.Stance))
            return false;
        var now = Worn(_copy);
        var wanted = changes
            .Where(c => c.Kind == ChangeKind.Equip && c.Subject != null)
            .ToDictionary(c => c.Subject!, c => c.Value, StringComparer.Ordinal);
        foreach (var change in previous)
            if (change.Kind == ChangeKind.Equip && change.Subject != null)
                wanted.TryAdd(change.Subject, _worn.GetValueOrDefault(change.Subject, Change.None));
        var differing = wanted
            .Where(slot => now.GetValueOrDefault(slot.Key, Change.None) != slot.Value)
            .Select(slot => slot.Key)
            .ToList();
        // An item going to another slot leaves its own first, as the gear is put back.
        var leaving = differing
            .Where(slot =>
                now.GetValueOrDefault(slot, Change.None) is var item
                && item != Change.None
                && differing.Any(other => other != slot && wanted[other] == item)
            )
            .ToList();
        if (leaving.Count > 0)
            Changes.Apply(
                _copy,
                ChangeList.Of(leaving.Select(slot => new Change(ChangeKind.Equip, slot, Change.None))),
                ignoreRequirements: true,
                throwOnRefusal: false
            );
        var moves = differing.Select(slot => new Change(ChangeKind.Equip, slot, wanted[slot])).ToList();
        var stance =
            changes.LastOrDefault(c => c.Kind == ChangeKind.Stance)?.Value
            ?? (previous.Any(c => c.Kind == ChangeKind.Stance) ? _stance : null);
        if (stance != null && _copy.Combat.Player.Stats.AttackStance.Value.Id != stance)
            moves.Add(new Change(ChangeKind.Stance, null, stance));
        if (moves.Count == 0)
            return true;
        _copy.Change(ChangeList.Of(moves), ignoreRequirements);
        return _copy.ChangeOutcomes?.All(o => !o.LeftOut) ?? true;
    }

    // Each unlocked slot with the item in it; a slot a two-slot item takes counts as empty.
    private static Dictionary<string, string> Worn(Copy copy)
    {
        using var scope = Guards.Enter();
        var equipment = copy.Combat.Player.Equipment;
        var worn = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in equipment.Config.EquipmentSlots)
        {
            if (!equipment.TryGetActiveSlot(slot, out var active))
                continue;
            worn[slot.Id] =
                active is ActiveEquipmentSlot { IsOccupied: false } && active.Equipped is { } equipped
                    ? equipped.Item.Id
                    : Change.None;
        }
        return worn;
    }
}
