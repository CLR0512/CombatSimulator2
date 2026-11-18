using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// Gear and levels before a side has been read for the first time: the page's own titles, each combat skill's row with
/// a bar for its level, the equipment and toolbelt grids with their slots empty, and a bar where each other part will
/// be, so the page has its shape from the start and the side's values land in place.
/// </summary>
internal static class GearSkeleton
{
    public static void Fill(
        VBoxContainer levels,
        VBoxContainer loadoutA,
        VBoxContainer loadoutB,
        int slotSize,
        int smallSlot
    )
    {
        levels.AddChild(Ui.Label(Tr.Get("Levels"), TextStyle.Label));
        // The skills and their steps are known before the side is: only the level waits, as the row will have it.
        var names = PanelState.Names();
        foreach (var skill in Changes.CombatSkills)
        {
            var brief = Ids.Brief(skill);
            var row = Ui.Add(levels, Ui.Row(6, "Cs2LevelWaiting" + brief));
            row.AddChild(Ui.Picture(ChangeOptions.Icon(skill), 22));
            var name = Ui.Label(names(skill), TextStyle.Body);
            name.CustomMinimumSize = new Vector2(96, 0);
            row.AddChild(name);
            row.AddChild(new Control { CustomMinimumSize = new Vector2(52, 0) });
            row.AddChild(Step(brief + "-", "−"));
            row.AddChild(Pending.Bar(40, 24));
            row.AddChild(Step(brief + "+", "+"));
            row.AddChild(Step(brief + "Max", Tr.Get("max")));
        }

        GameSlot.Title(loadoutA, Tr.Get("Equipment"), "Cs2Gear");
        var grid = ChangeOptions.Grid();
        if (grid.Count > 0)
        {
            var columns = grid.Max(g => g.Column) + 1;
            var rows = grid.Max(g => g.Row) + 1;
            var holder = Ui.Add(loadoutA, new GridContainer { Name = "Cs2Gear", Columns = columns });
            holder.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            holder.AddThemeConstantOverride("h_separation", 8);
            holder.AddThemeConstantOverride("v_separation", 8);
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
                holder.AddChild(
                    grid.FirstOrDefault(g => g.Column == c && g.Row == r) is { } cell
                        ? Slot("Cs2Slot" + Ids.Brief(cell.Slot), slotSize, ChangeOptions.Icon(cell.Slot))
                        : new Control { CustomMinimumSize = new Vector2(slotSize, slotSize) }
                );
        }
        GameSlot.Title(loadoutA, Tr.Get("Toolbelt"), "Cs2Toolbelt");
        var belt = Ui.Add(loadoutA, Ui.Row(8));
        belt.Alignment = BoxContainer.AlignmentMode.Center;
        var n = 0;
        foreach (var slot in ChangeOptions.Toolbelt())
            belt.AddChild(
                Slot(
                    "Cs2ToolbeltWaiting" + (++n).ToString(CultureInfo.InvariantCulture),
                    smallSlot,
                    slot.Unlocked ? ChangeOptions.Icon(slot.FitsType) : NativeStyles.LockIcon
                )
            );

        foreach (
            var (title, name, height) in new[]
            {
                (Tr.Get("Stance"), "Cs2Stance", 96f),
                (Tr.Get("Prayers"), "Cs2Prayers", 40f),
                (Tr.Get("Food"), "Cs2Food", 60f),
                (Tr.Get("Spell"), "Cs2Spell", 72f),
                (Tr.Get("Rune Costs"), "Cs2Runes", 24f),
            }
        )
        {
            GameSlot.Title(loadoutB, title, name);
            loadoutB.AddChild(Pending.Bar(0, height));
        }
    }

    private static Button Step(string name, string text)
    {
        var button = Ui.Button(text, Look.Button4, "Cs2LevelWaiting" + name);
        button.Disabled = true;
        return button;
    }

    private static Button Slot(string name, int size, Texture2D? icon) =>
        GameSlot.Make(name, size, NativeStyles.Slot, icon, true, false, null, "", true);
}
