using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// A side as the Setup tab draws it: its latest check when that is for the changes shown, else its base with the
/// changes laid over it until the next check answers, so an edit shows at once.
/// </summary>
internal sealed class SideView(ChangeCheckResult check, ChangeList changes)
{
    public ChangeList Changes { get; } = changes;
    public bool Current { get; } = check.Changes.Equals(changes);

    /// <summary>The side before its changes: what a change's "was" shows.</summary>
    public SetupState Base { get; } = check.Before;

    public int Applied => Current ? check.Outcomes.Count(o => o.Applies) : 0;
    public int Refused => Current ? check.Outcomes.Count(o => o.LeftOut) : 0;

    public ChangeOutcome? Outcome(Change change) =>
        Current ? check.Outcomes.FirstOrDefault(o => o.Change == change) : null;

    public bool Changed(ChangeKey key) => Changes.Find(key) != null;

    // What the base has for a change's key, as the from-value before a check answers.
    public string? From(Change change) =>
        change.Kind switch
        {
            ChangeKind.Level => Base
                .Levels.FirstOrDefault(l => l.Skill == change.Subject)
                ?.Level.ToString(CultureInfo.InvariantCulture),
            ChangeKind.Equip => Base.Slots.FirstOrDefault(s => s.Slot == change.Subject)?.Item ?? Change.None,
            ChangeKind.Stance => Base.Stance,
            ChangeKind.Spell => Base.Spell ?? Change.None,
            ChangeKind.Imbuement => Base.Imbuement ?? Change.None,
            ChangeKind.Food => Base.Food ?? Change.None,
            ChangeKind.Potion => Base.Potion ?? Change.None,
            ChangeKind.Toolbelt => Base.Toolbelt.FirstOrDefault(s => s.Slot == change.Subject)?.Item ?? Change.None,
            // The save gives no monster a loadout of its own; a task choice has no value in the save to come from.
            ChangeKind.Loadouts => Change.None,
            // Read from the side's check copy, which the base doesn't carry.
            ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor or ChangeKind.Pet or ChangeKind.Bonfire => null,
            ChangeKind.Technique => null,
            _ => Base.Prayers.FirstOrDefault(p => p.Prayer == change.Subject)?.On == true ? Change.On : Change.Off,
        };

    // The last checked B stands until the next check answers; only a key edited since that check shows its edit
    // (or the base's value, when the edit was undone).
    private bool Edited(ChangeKey key) => !Equals(check.Changes.Find(key), Changes.Find(key));

    private SetupState Checked => check.After;

    public int Level(string skill)
    {
        var key = new ChangeKey(ChangeKind.Level, skill);
        if (Edited(key))
            return Changes.Find(key)?.Level ?? Base.Levels.FirstOrDefault(l => l.Skill == skill)?.Level ?? 0;
        return Checked.Levels.FirstOrDefault(l => l.Skill == skill)?.Level ?? 0;
    }

    public string? Item(string slot)
    {
        var key = new ChangeKey(ChangeKind.Equip, slot);
        if (Edited(key))
            return Changes.Find(key) is { } change ? change.Item : Base.Slots.FirstOrDefault(s => s.Slot == slot)?.Item;
        return Checked.Slots.FirstOrDefault(s => s.Slot == slot)?.Item;
    }

    /// <summary>
    /// What the item shown in an equipment (or <paramref name="toolbelt"/>) slot needs that the side doesn't have, as
    /// the side's check found it; empty when it can be worn, or while the check hasn't caught up with the slot.
    /// </summary>
    public IReadOnlyList<Note> Unmet(string slot, bool toolbelt = false)
    {
        var shown = toolbelt ? ToolbeltItem(slot) : Item(slot);
        var held = (toolbelt ? Checked.Toolbelt : Checked.Slots).FirstOrDefault(s => s.Slot == slot);
        return shown != null && held?.Item == shown ? held.Unmet : [];
    }

    /// <summary>What an active prayer needs that the side doesn't have, as the side's check found it; else empty.</summary>
    public IReadOnlyList<Note> PrayerUnmet(string prayer) =>
        Checked.Prayers.FirstOrDefault(p => p.Prayer == prayer) is { On: true } state ? state.Locked : [];

    /// <summary>What a toolbelt slot holds, as <see cref="Item"/> reads an equipment slot.</summary>
    public string? ToolbeltItem(string slot)
    {
        var key = new ChangeKey(ChangeKind.Toolbelt, slot);
        if (Edited(key))
            return Changes.Find(key) is { } change
                ? change.Item
                : Base.Toolbelt.FirstOrDefault(s => s.Slot == slot)?.Item;
        return Checked.Toolbelt.FirstOrDefault(s => s.Slot == slot)?.Item;
    }

    public string? Potion => One(ChangeKind.Potion, Checked.Potion, Base.Potion);

    public string? Stance => One(ChangeKind.Stance, Checked.Stance, Base.Stance);

    /// <summary>The stances the side's weapon has, as last checked (a new weapon's show once its check answers).</summary>
    public IReadOnlyList<string> Stances => Checked.Stances;

    public string? Food => One(ChangeKind.Food, Checked.Food, Base.Food);

    public string? Spell => One(ChangeKind.Spell, Checked.Spell, Base.Spell);

    public string? Imbuement => One(ChangeKind.Imbuement, Checked.Imbuement, Base.Imbuement);

    public IReadOnlyList<RuneCost> RuneCosts => Checked.RuneCosts;

    public long PrayerPoints => Checked.PrayerPoints;

    public IReadOnlyList<PrayerSlotState> PrayerSlots => Checked.PrayerSlots;

    // A kind with one value: the checked one, or the edit made since (null for none), or the base's when undone.
    private string? One(ChangeKind kind, string? checkedValue, string? a)
    {
        var key = new ChangeKey(kind, null);
        if (!Edited(key))
            return checkedValue;
        return Changes.Find(key) is { } change ? (change.Value == Model.Change.None ? null : change.Value) : a;
    }

    /// <summary>The prayer in each prayer slot, in slot order (null for an empty one).</summary>
    public List<string?> PrayersInSlots()
    {
        var prayerEdits = Changes.Where(c => c.Kind == ChangeKind.Prayer).Select(c => c.ShortForm);
        var prayerChecked = check.Changes.Where(c => c.Kind == ChangeKind.Prayer).Select(c => c.ShortForm);
        if (prayerEdits.SequenceEqual(prayerChecked))
            return [.. Checked.PrayerSlots.Select(s => s.Prayer)];
        // Prayers edited since the check: the base's slots with the edits laid over them.
        var slots = Base.PrayerSlots.Select(s => s.Prayer).ToList();
        foreach (var change in Changes.Where(c => c.Kind == ChangeKind.Prayer && !c.TurnsOn))
        {
            var at = slots.IndexOf(change.Subject);
            if (at >= 0)
                slots[at] = null;
        }
        foreach (var change in Changes.Where(c => c.TurnsOn))
        {
            if (slots.Contains(change.Subject))
                continue;
            var free = slots.IndexOf(null);
            if (free >= 0 && Base.PrayerSlots[free].Unlocked)
                slots[free] = change.Subject;
        }
        return slots;
    }

    /// <summary>The changes with the side praying exactly <paramref name="wanted"/>: on and off changes against its base.</summary>
    public ChangeList WithPrayers(IEnumerable<string> wanted)
    {
        var want = wanted.ToHashSet(StringComparer.Ordinal);
        var had = Base.Prayers.Where(p => p.On).Select(p => p.Prayer).ToHashSet(StringComparer.Ordinal);
        var list = Model.ChangeList.Of(Changes.Where(c => c.Kind != ChangeKind.Prayer));
        foreach (var prayer in had.Except(want))
            list = list.With(new Change(ChangeKind.Prayer, prayer, Model.Change.Off));
        foreach (var prayer in want.Except(had))
            list = list.With(new Change(ChangeKind.Prayer, prayer, Model.Change.On));
        return list;
    }
}
