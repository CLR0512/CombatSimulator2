using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.SetupTab;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The small item picker of the Simulate tab: a click on a slot of a side's card, or on its stance, food, a prayer or
/// its spell beside the slots, opens it right under what was clicked, where it stays as the card moves, and a pick
/// changes the side without leaving the tab. A click anywhere else, the
/// same slot again, Escape, a run starting or the tab hiding closes it. A right-click on a slot steps it down.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class SlotPicker
{
    private readonly Control _view;
    private readonly Control _layer;
    private readonly SetupColumn _cards;
    private readonly ItemPicker _picker = new(compact: true);
    private readonly Control _catcher;
    private (SideLetter Letter, string Slot) _picked;

    // Set while the picker is open under a control outside the cards: the gear search's "+ Item".
    private Control? _under;

    /// <summary>
    /// A picker drawn in <paramref name="layer"/>, the layer over <paramref name="view"/>, for the slots of
    /// <paramref name="cards"/>.
    /// </summary>
    public SlotPicker(Control view, Control layer, SetupColumn cards)
    {
        _view = view;
        _layer = layer;
        _cards = cards;
        // Under the picker while it is open: a click anywhere else closes it.
        _catcher = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _catcher.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
            {
                _picker.Close();
                _catcher.AcceptEvent();
            }
        };
        layer.AddChild(_catcher);
        layer.AddChild(_picker.Root);
    }

    // The card's lines go by these keys where its slots go by their ids, which never start so.
    private const string LinePrefix = "line:";
    public const string StanceLine = LinePrefix + "stance";
    public const string FoodLine = LinePrefix + "food";
    public const string SpellLine = LinePrefix + "spell";
    private const string PrayerPrefix = LinePrefix + "prayer:";

    /// <summary>The key of the card's line for <paramref name="prayer"/>, or of its "No prayers" line.</summary>
    public static string PrayerLine(string? prayer) => PrayerPrefix + prayer;

    public static bool IsPrayerLine(string key) => key.StartsWith(PrayerPrefix, StringComparison.Ordinal);

    /// <summary>The slot the picker is open for, as <c>cs2 ui</c> prints it; <c>closed</c> when it isn't.</summary>
    public string For => _picker.For;

    /// <summary>
    /// Opens under <paramref name="under"/> with every item the character owns that can be worn;
    /// <paramref name="picked"/> gets the one chosen. Closes when open under it already.
    /// </summary>
    public void PickItem(Control under, Action<string> picked)
    {
        if (_picker.Open && ReferenceEquals(_under, under))
        {
            _picker.Close();
            return;
        }
        if (RunService.Instance.IsActive)
            return;
        _under = under;
        var options = ChangeOptions
            .Grid()
            .SelectMany(g => ChangeOptions.For(ChangeKind.Equip, g.Slot))
            .Where(o => o.Owned || o.WornInA)
            .DistinctBy(o => o.Id)
            .ToList();
        _picker.Show(
            "item",
            Tr.Get("Never use"),
            "",
            Tr.Get("Search items"),
            null,
            options,
            id =>
            {
                _picker.Close();
                picked(id);
            }
        );
    }

    /// <summary>
    /// Opens for <paramref name="slot"/> of <paramref name="letter"/>'s side, a slot's id or a line's key; closes
    /// when open on it.
    /// </summary>
    public void Pick(SideLetter letter, string slot)
    {
        _under = null;
        if (_picker.Open && _picked == (letter, slot))
        {
            _picker.Close();
            return;
        }
        var side = letter == SideLetter.A ? PanelState.A : PanelState.B;
        var check =
            (letter == SideLetter.A ? PanelState.CheckA : PanelState.CheckB)
            ?? (side != null ? SideChecker.Latest(letter, side) : null);
        if (side is not { CanChange: true } || check == null || RunService.Instance.IsActive)
            return;
        var view = new SideView(check.Changes, side.Changes);
        if (slot.StartsWith(LinePrefix, StringComparison.Ordinal))
        {
            PickLine(letter, slot, side, view);
            return;
        }
        var had = view.Base.Slots.FirstOrDefault(s => s.Slot == slot)?.Item;
        _picked = (letter, slot);
        _picker.Show(
            slot,
            PanelState.Names()(slot),
            "",
            Tr.Get("Search items"),
            Tr.Get("Empty slot"),
            ChangeOptions.For(ChangeKind.Equip, slot),
            id =>
            {
                var change = new Change(ChangeKind.Equip, slot, id);
                var changes = id == (had ?? Change.None) ? side.Changes.Without(change.Key) : side.Changes.With(change);
                PanelState.SetSide(letter, side.WithChanges(changes));
            },
            view.Item(slot),
            items: true
        );
    }

    // The stance, food, a prayer or the spell, picked as on the Setup tab.
    private void PickLine(SideLetter letter, string key, SideState side, SideView view)
    {
        var names = PanelState.Names();
        void Edit(Change change, string? had) =>
            PanelState.SetSide(
                letter,
                side.WithChanges(
                    change.Value == (had ?? Change.None) ? side.Changes.Without(change.Key) : side.Changes.With(change)
                )
            );
        if (key == StanceLine)
        {
            _picked = (letter, key);
            _picker.Show(
                key,
                Tr.Get("Stance"),
                "",
                Tr.Get("Search stances"),
                null,
                [.. view.Stances.Select(id => new ChangeOption(id, names(id), null, false, false, []))],
                id => Edit(new Change(ChangeKind.Stance, null, id), view.Base.Stance)
            );
        }
        else if (key == FoodLine)
        {
            _picked = (letter, key);
            _picker.Show(
                key,
                Tr.Get("Food"),
                "",
                Tr.Get("Search food"),
                Tr.Get("No food"),
                ChangeOptions.For(ChangeKind.Food, null),
                id => Edit(new Change(ChangeKind.Food, null, id), view.Base.Food),
                view.Food,
                items: true
            );
        }
        else if (key == SpellLine)
        {
            _picked = (letter, key);
            _picker.Show(
                key,
                Tr.Get("Spell"),
                "",
                Tr.Get("Search spells"),
                null,
                ChangeOptions.For(
                    ChangeKind.Spell,
                    null,
                    [.. view.Base.Slots.Select(s => view.Item(s.Slot)).OfType<string>()]
                ),
                id => Edit(new Change(ChangeKind.Spell, null, id), view.Base.Spell)
            );
        }
        else if (IsPrayerLine(key))
        {
            // The slot the prayer is in, or for "No prayers" and "+ Prayer" the first unlocked one left empty.
            var shown = view.PrayersInSlots();
            var prayer = key[PrayerPrefix.Length..];
            var index =
                prayer.Length > 0
                    ? shown.IndexOf(prayer)
                    : Enumerable
                        .Range(0, shown.Count)
                        .FirstOrDefault(i => view.PrayerSlots[i].Unlocked && shown[i] == null, -1);
            if (index < 0)
                return;
            _picked = (letter, key);
            _picker.Show(
                key,
                Tr.Format("Prayer slot {0}", index + 1),
                "",
                Tr.Get("Search prayers"),
                Tr.Get("Empty slot"),
                ChangeOptions.For(ChangeKind.Prayer, null),
                id =>
                {
                    var wanted = shown.ToList();
                    wanted[index] = id == Change.None ? null : id;
                    PanelState.SetSide(letter, side.WithChanges(view.WithPrayers(wanted.OfType<string>().Distinct())));
                }
            );
        }
    }

    /// <summary>
    /// A right-click steps <paramref name="slot"/> down once, as on the Setup tab: a changed slot goes back to what
    /// the character has, and a slot holding the character's own goes empty.
    /// </summary>
    public void StepDown(SideLetter letter, string slot)
    {
        var side = letter == SideLetter.A ? PanelState.A : PanelState.B;
        var check =
            (letter == SideLetter.A ? PanelState.CheckA : PanelState.CheckB)
            ?? (side != null ? SideChecker.Latest(letter, side) : null);
        if (side is not { CanChange: true } || check == null || RunService.Instance.IsActive)
            return;
        _picker.Close();
        // The card marks every slot whose item changed, so a slot can be gold through a change kept under another
        // (a two-handed weapon fills two): putting it back undoes that change.
        var behind = side.Changes.EquipChangeBehind(
            slot,
            check.Changes.Before.Slots.ToDictionary(s => s.Slot, s => s.Item, StringComparer.Ordinal),
            check.Changes.After.Slots.ToDictionary(s => s.Slot, s => s.Item, StringComparer.Ordinal)
        );
        if (behind != null)
            PanelState.SetSide(letter, side.WithChanges(side.Changes.Without(behind.Key)));
        else if (new SideView(check.Changes, side.Changes).Item(slot) != null)
            PanelState.SetSide(
                letter,
                side.WithChanges(side.Changes.With(new Change(ChangeKind.Equip, slot, Change.None)))
            );
    }

    /// <summary>
    /// Once a frame: keeps the open picker under its slot, above it where there's no room below, and closes it
    /// when the view isn't <paramref name="shown"/> or a run is going.
    /// </summary>
    public void Place(bool shown)
    {
        if (!_picker.Open)
        {
            _catcher.Visible = false;
            return;
        }
        if (!shown || RunService.Instance.IsActive)
        {
            _picker.Close();
            return;
        }
        _catcher.Visible = true;
        _catcher.Position = -_layer.Position;
        _catcher.Size = _view.Size;
        if (
            (_under != null && GodotObject.IsInstanceValid(_under) ? _under : _cards.Slot(_picked.Letter, _picked.Slot))
            is not { } slot
        )
            return;
        var toLayer = _layer.GetGlobalTransform().AffineInverse();
        var rect = slot.GetGlobalRect();
        var size = _picker.Root.GetCombinedMinimumSize();
        _picker.Root.Size = size;
        // The slot's own space past its box: the picker's top is then level with the next row's boxes. A line has none.
        var gap =
            _under != null || _picked.Slot.StartsWith(LinePrefix, StringComparison.Ordinal)
                ? 2
                : Widgets.MiniSlot.Inset(slot.Size.X);
        // In by the same space, so its left edge stands on the slot's box, in line with the grid. Under a button (the
        // Gear tab's "+ Item", at the end of its row) it hangs from the button's right edge instead.
        var edge = _under != null ? rect.End.X : rect.Position.X;
        var shift = _under != null ? -size.X : gap;
        var below = toLayer * new Vector2(edge, rect.End.Y) + new Vector2(shift, gap);
        var above = toLayer * new Vector2(edge, rect.Position.Y) + new Vector2(shift, -size.Y - gap);
        var at = below.Y + size.Y <= _view.Size.Y - _layer.Position.Y ? below : above;
        var left = -_layer.Position.X;
        var right = _view.Size.X - _layer.Position.X - size.X;
        _picker.Root.Position = new Vector2(Math.Clamp(at.X, left, Math.Max(left, right)), at.Y);
    }
}
