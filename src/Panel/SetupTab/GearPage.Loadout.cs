using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

// The loadout column: equipment, toolbelt, stance, prayers, food, spell and runes, and their pickers.
internal sealed partial class GearPage
{
    private void Equipment(SideView view)
    {
        GameSlot.Title(_loadoutA, Tr.Get("Equipment"), "Cs2Gear");
        var grid = ChangeOptions.Grid();
        if (grid.Count == 0)
            return;
        var columns = grid.Max(g => g.Column) + 1;
        var rows = grid.Max(g => g.Row) + 1;
        var holder = Ui.Add(_loadoutA, new GridContainer { Name = "Cs2Gear", Columns = columns });
        holder.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        holder.AddThemeConstantOverride("h_separation", 8);
        holder.AddThemeConstantOverride("v_separation", 8);
        var at = grid.ToDictionary(g => (g.Column, g.Row));
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                if (!at.TryGetValue((c, r), out var cell))
                {
                    holder.AddChild(new Control { CustomMinimumSize = new Vector2(SlotSize, SlotSize) });
                    continue;
                }
                var slot = cell.Slot;
                var item = view.Item(slot);
                var key = new ChangeKey(ChangeKind.Equip, slot);
                var changed = view.Changed(key);
                var name = Name(slot);
                var had = view.Base.Slots.FirstOrDefault(s => s.Slot == slot)?.Item;
                var unmet = Unmet(view, slot);
                holder.AddChild(
                    GameSlot.Make(
                        "Cs2Slot" + Ids.Brief(slot),
                        SlotSize,
                        NativeStyles.Slot,
                        ChangeOptions.Icon(item ?? slot),
                        item == null,
                        changed,
                        () => PickGear(view, slot),
                        Hover(
                            item == null ? name : string.Join(" · ", name, Name(item)),
                            Outcome(view, key),
                            changed ? (had == null ? Tr.Get("empty") : Name(had)) : null,
                            unmet
                        ),
                        _readOnly,
                        StepDown(view, key, item),
                        unmet.Count > 0
                    )
                );
            }
        }
    }

    // A right-click steps a slot down once: a changed slot goes back to what the character has, and a slot holding
    // the character's own goes empty. Nothing for a slot already empty and unchanged.
    private (string, Action)? StepDown(SideView view, ChangeKey key, string? item)
    {
        if (view.Changes.Find(key) != null)
            return (Tr.Get("Right-click: put back"), () => SetChanges(view.Changes.Without(key)));
        if (item == null)
            return null;
        return (
            Tr.Get("Right-click: empty"),
            () => SetChanges(view.Changes.With(new Change(key.Kind, key.Subject, Change.None)))
        );
    }

    private void PickGear(SideView view, string slot)
    {
        _pickAgain = v => PickGear(v, slot);
        var a = view.Base.Slots.FirstOrDefault(s => s.Slot == slot)?.Item;
        var now = view.Item(slot);
        _picker.Show(
            slot,
            Name(slot),
            Tr.Format("Pick an item for {0}. Now: {1}.", [Letter, now == null ? Tr.Get("empty") : Name(now)]),
            Tr.Get("Search items"),
            Tr.Get("Empty slot"),
            ChangeOptions.For(ChangeKind.Equip, slot),
            id => Edit(view, new Change(ChangeKind.Equip, slot, id), id == (a ?? Change.None)),
            now,
            items: true
        );
    }

    // The toolbelt: each unlocked slot holds what it holds for the side and opens the picker; a locked one shows its lock.
    private void Toolbelt(SideView view)
    {
        GameSlot.Title(_loadoutA, Tr.Get("Toolbelt"), "Cs2Toolbelt");
        var row = Ui.Add(_loadoutA, Ui.Row(4, "Cs2Toolbelt"));
        row.Alignment = BoxContainer.AlignmentMode.Center;
        var locked = 0;
        foreach (var slot in ChangeOptions.Toolbelt())
        {
            if (!slot.Unlocked)
            {
                locked++;
                row.AddChild(
                    GameSlot.Make(
                        "Cs2ToolbeltLocked" + locked.ToString(CultureInfo.InvariantCulture),
                        SmallSlot,
                        NativeStyles.Slot,
                        NativeStyles.LockIcon,
                        false,
                        false,
                        null,
                        Tr.Get("Locked"),
                        _readOnly
                    )
                );
                continue;
            }
            var potion = slot.FitsType == Changes.CombatPotionSlot;
            var key = potion ? new ChangeKey(ChangeKind.Potion, null) : new ChangeKey(ChangeKind.Toolbelt, slot.Slot);
            var item = potion ? view.Potion : view.ToolbeltItem(slot.Slot);
            var had = potion ? view.Base.Potion : view.Base.Toolbelt.FirstOrDefault(s => s.Slot == slot.Slot)?.Item;
            var changed = view.Changed(key);
            var usable = !potion || view.Base.PotionsInBuild;
            var unmet = potion ? [] : Unmet(view, slot.Slot, toolbelt: true);
            var name =
                !usable ? Tr.Get("No combat potions in this build")
                : item == null ? Name(slot.Slot)
                : string.Join(" · ", Name(slot.Slot), Name(item));
            row.AddChild(
                GameSlot.Make(
                    potion ? "Cs2Potion" : "Cs2Toolbelt" + Ids.Brief(slot.Slot),
                    SmallSlot,
                    NativeStyles.Slot,
                    ChangeOptions.Icon(item ?? slot.FitsType),
                    item == null,
                    changed,
                    usable ? () => PickToolbelt(view, slot.Slot, potion, had) : null,
                    Hover(
                        name,
                        Outcome(view, key),
                        changed ? (had == null ? Tr.Get("empty") : Name(had)) : null,
                        unmet
                    ),
                    _readOnly,
                    usable ? StepDown(view, key, item) : null,
                    unmet.Count > 0
                )
            );
        }
    }

    private void PickToolbelt(SideView view, string slot, bool potion, string? had)
    {
        _pickAgain = v => PickToolbelt(v, slot, potion, had);
        var now = potion ? view.Potion : view.ToolbeltItem(slot);
        _picker.Show(
            potion ? "potion" : slot,
            Name(slot),
            Tr.Format("Pick an item for {0}. Now: {1}.", [Letter, now == null ? Tr.Get("empty") : Name(now)]),
            Tr.Get("Search items"),
            Tr.Get("Empty slot"),
            potion ? ChangeOptions.For(ChangeKind.Potion, null) : ChangeOptions.For(ChangeKind.Toolbelt, slot),
            id =>
                Edit(
                    view,
                    potion ? new Change(ChangeKind.Potion, null, id) : new Change(ChangeKind.Toolbelt, slot, id),
                    id == (had ?? Change.None)
                ),
            now,
            items: true
        );
    }

    // What the latest check made of the side's change to a control, when there is one.
    private static ChangeOutcome? Outcome(SideView view, ChangeKey key) =>
        view.Changes.Find(key) is { } change ? view.Outcome(change) : null;

    private void Stance(SideView view)
    {
        GameSlot.Title(_loadoutB, Tr.Get("Stance"), "Cs2Stance");
        var grid = Ui.Add(_loadoutB, new GridContainer { Name = "Cs2Stance", Columns = 2 });
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        var stance = view.Stance;
        foreach (var id in view.Stances)
        {
            var button = Ui.Button(
                Name(id),
                Look.Button8,
                "Cs2Stance" + Ids.Brief(id),
                () => Edit(view, new Change(ChangeKind.Stance, null, id), id == view.Base.Stance)
            );
            button.ToggleMode = true;
            button.SetPressedNoSignal(id == stance);
            button.Icon = ChangeOptions.Icon(id);
            button.ExpandIcon = false;
            button.AddThemeConstantOverride("icon_max_width", 20);
            button.CustomMinimumSize = new Vector2(138, 0);
            button.Disabled = _readOnly;
            grid.AddChild(button);
        }
    }

    private void Prayers(SideView view)
    {
        var points = Ui.Row(4, "Cs2PrayerPoints");
        points.AddChild(Ui.Picture(ChangeOptions.Icon("MelvorBase:PrayerPoints"), 16));
        points.AddChild(Ui.Label(Ui.Number(view.PrayerPoints), TextStyle.Small));
        GameSlot.Title(_loadoutB, Tr.Get("Prayers"), "Cs2Prayers", points);
        var row = Ui.Add(_loadoutB, Ui.Row(4, "Cs2Prayers"));
        row.Alignment = BoxContainer.AlignmentMode.Center;
        var shown = view.PrayersInSlots();
        for (var i = 0; i < view.PrayerSlots.Count; i++)
        {
            var slot = view.PrayerSlots[i];
            var index = i;
            var prayer = shown[i];
            var changed = prayer != null && view.Changed(new ChangeKey(ChangeKind.Prayer, prayer));
            var unmet = PrayerUnmet(view, prayer);
            row.AddChild(
                GameSlot.Make(
                    "Cs2PrayerSlot" + (i + 1).ToString(CultureInfo.InvariantCulture),
                    PrayerSlot,
                    NativeStyles.Prayer,
                    !slot.Unlocked ? NativeStyles.LockIcon
                        : prayer != null ? ChangeOptions.Icon(prayer)
                        : NativeStyles.EmptyPrayerIcon,
                    prayer == null,
                    changed,
                    slot.Unlocked ? () => PickPrayer(view, index) : null,
                    prayer != null ? Hover(Name(prayer), null, null, unmet) : Tr.Get("Empty prayer slot"),
                    _readOnly,
                    prayer != null
                        ? (
                            Tr.Get("Right-click: empty"),
                            () =>
                                SetChanges(
                                    view.WithPrayers(shown.Where((_, at) => at != index).OfType<string>().Distinct())
                                )
                        )
                        : null,
                    unmet.Count > 0
                )
            );
        }
    }

    private void PickPrayer(SideView view, int index)
    {
        _pickAgain = v => PickPrayer(v, index);
        var shown = view.PrayersInSlots();
        var now = shown[index];
        _picker.Show(
            "prayer",
            Tr.Format("Prayer slot {0}", index + 1),
            Tr.Format("Pick a prayer for {0}. Now: {1}.", [Letter, now == null ? Tr.Get("none") : Name(now)]),
            Tr.Get("Search prayers"),
            Tr.Get("Empty slot"),
            ChangeOptions.For(ChangeKind.Prayer, null),
            id =>
            {
                var wanted = shown.ToList();
                wanted[index] = id == Change.None ? null : id;
                SetChanges(view.WithPrayers(wanted.OfType<string>().Distinct()));
            }
        );
    }

    private void Food(SideView view)
    {
        GameSlot.Title(_loadoutB, Tr.Get("Food"), "Cs2Food");
        var row = Ui.Add(_loadoutB, Ui.Row(10));
        var food = view.Food;
        var changed = view.Changed(new ChangeKey(ChangeKind.Food, null));
        row.AddChild(
            GameSlot.Make(
                "Cs2Food",
                SmallSlot,
                NativeStyles.Slot,
                food != null ? ChangeOptions.Icon(food) : null,
                food == null,
                changed,
                () => PickFood(view),
                Tr.Get("Pick other food"),
                _readOnly
            )
        );
        var names = Ui.Add(row, Ui.Column(0));
        names.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        names.AddChild(Ui.Label(food != null ? Name(food) : Tr.Get("No food"), TextStyle.Body));
    }

    private void PickFood(SideView view)
    {
        _pickAgain = PickFood;
        _picker.Show(
            "food",
            Tr.Get("Food"),
            Tr.Format("Pick food for {0}. Now: {1}.", [Letter, view.Food == null ? Tr.Get("none") : Name(view.Food)]),
            Tr.Get("Search food"),
            Tr.Get("No food"),
            ChangeOptions.For(ChangeKind.Food, null),
            id => Edit(view, new Change(ChangeKind.Food, null, id), id == (view.Base.Food ?? Change.None)),
            view.Food,
            items: true
        );
    }

    private void Spell(SideView view)
    {
        GameSlot.Title(_loadoutB, Tr.Get("Spell"), "Cs2Spell");
        var row = Ui.Add(_loadoutB, Ui.Row(24, "Cs2Spell"));
        row.Alignment = BoxContainer.AlignmentMode.Center;
        var spell = view.Spell;
        var imbuement = view.Imbuement;
        row.AddChild(
            Captioned(
                GameSlot.Make(
                    "Cs2Spell",
                    SmallSlot,
                    NativeStyles.Slot,
                    spell != null ? ChangeOptions.Icon(spell) : ChangeOptions.Icon("MelvorBase:Magic"),
                    spell == null,
                    view.Changed(new ChangeKey(ChangeKind.Spell, null)),
                    () => PickSpell(view, ChangeKind.Spell),
                    Tr.Get("Spell"),
                    _readOnly
                ),
                spell != null ? Name(spell) : Tr.Get("No spell")
            )
        );
        row.AddChild(
            Captioned(
                GameSlot.Make(
                    "Cs2Imbuement",
                    SmallSlot,
                    NativeStyles.Slot,
                    imbuement != null ? ChangeOptions.Icon(imbuement) : ChangeOptions.Icon("MelvorBase:Magic"),
                    imbuement == null,
                    view.Changed(new ChangeKey(ChangeKind.Imbuement, null)),
                    () => PickSpell(view, ChangeKind.Imbuement),
                    Tr.Get("Imbuement"),
                    _readOnly,
                    StepDown(view, new ChangeKey(ChangeKind.Imbuement, null), imbuement)
                ),
                imbuement != null ? Name(imbuement) : Tr.Get("No imbuement")
            )
        );
    }

    private static VBoxContainer Captioned(Control slot, string caption)
    {
        var column = Ui.Column(2);
        column.AddChild(slot);
        var label = Ui.Label(caption, TextStyle.Tiny);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(label);
        return column;
    }

    private void PickSpell(SideView view, ChangeKind kind)
    {
        _pickAgain = v => PickSpell(v, kind);
        var spell = kind == ChangeKind.Spell;
        var now = spell ? view.Spell : view.Imbuement;
        var a = spell ? view.Base.Spell : view.Base.Imbuement;
        _picker.Show(
            spell ? "spell" : "imbuement",
            spell ? Tr.Get("Spell") : Tr.Get("Imbuement"),
            spell
                ? Tr.Format("Pick a spell for {0}. Now: {1}.", [Letter, now == null ? Tr.Get("none") : Name(now)])
                : Tr.Format("Pick an imbuement for {0}. Now: {1}.", [Letter, now == null ? Tr.Get("none") : Name(now)]),
            spell ? Tr.Get("Search spells") : Tr.Get("Search imbuements"),
            spell ? null : Tr.Get("No imbuement"),
            ChangeOptions.For(kind, null, [.. view.Base.Slots.Select(s => view.Item(s.Slot)).OfType<string>()]),
            id => Edit(view, new Change(kind, null, id), id == (a ?? Change.None))
        );
    }

    private void Runes(SideView view)
    {
        GameSlot.Title(_loadoutB, Tr.Get("Rune Costs"), "Cs2Runes");
        var runes = Ui.Add(_loadoutB, Ui.Column(2, "Cs2Runes"));
        var costs = view.RuneCosts;
        if (costs.Count == 0)
        {
            var none = Ui.Label(Tr.Get("No spell selected"), TextStyle.Small);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            runes.AddChild(none);
            return;
        }
        var n = 0;
        foreach (var cost in costs)
        {
            n++;
            var row = Ui.Add(runes, Ui.Row(6, "Cs2Rune" + n.ToString(CultureInfo.InvariantCulture)));
            row.AddChild(Ui.Picture(ChangeOptions.Icon(cost.Item), 20));
            var name = Ui.Label(Name(cost.Item), TextStyle.Small);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Ui.Label(cost.PerCast.ToString(Tr.Culture), TextStyle.Small));
            // Translators: After a rune's count in the spell's costs: that many are used each time it is cast.
            // The count's own size, so the two share a baseline; dimmed to stay quiet beside it.
            var unit = Ui.Label(Tr.Get("/cast"), TextStyle.Small);
            unit.Modulate = Colors.White with { A = 0.5f };
            row.AddChild(unit);
        }
    }
}
