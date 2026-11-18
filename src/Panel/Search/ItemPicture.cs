using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>An item as an answer draws it: its slot picture, and what the game says about it on hover.</summary>
internal static class ItemPicture
{
    // An item's slot picture, with what the game says about the item on hover: its stats, against the item it
    // replaces when there is one.
    public static PanelContainer Of(string? item, string slot, string? replaces)
    {
        var picture = Slot.Make(ChangeOptions.Icon(item ?? slot), 32);
        if (item != null)
        {
            picture.MouseFilter = Control.MouseFilterEnum.Stop;
            InstantTooltips.Lazy(picture, () => About(item, replaces));
        }
        return picture;
    }

    public static string About(string item, string? replaces)
    {
        var about = ChangeOptions.About(item, replaces);
        var name = PanelState.Names()(item);
        return about.Count > 0 ? name + "\n" + string.Join("\n", about.Select(Tr.Note)) : name;
    }

    /// <summary>
    /// A set's gain, then an icon for each item it puts on and each choice it changes, each named on hover (an item
    /// with its stats), wrapping within the width it is given; its stance in words.
    /// </summary>
    public static HFlowContainer Strip(SetResult set)
    {
        var strip = new HFlowContainer
        {
            Name = "GearSearchSetStrip",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        strip.AddThemeConstantOverride("h_separation", 4);
        strip.AddThemeConstantOverride("v_separation", 2);
        var gain = Ui.Add(strip, Ui.Label(SearchWords.Gain(set.Percent, set.Gain), TextStyle.Small));
        gain.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        AddIcons(strip, set);
        return strip;
    }

    /// <summary>
    /// Adds to <paramref name="parent"/> an icon for each item a set puts on (nothing as its slot) and each choice it
    /// changes, then its stance in words.
    /// </summary>
    public static void AddIcons(Container parent, SetResult set)
    {
        foreach (var slot in set.Slots)
            parent.AddChild(Of(slot.New, slot.Slot, slot.Old));
        foreach (var choice in set.Choices.Where(c => !c.Keep))
        foreach (var (id, name) in choice.New.Zip(choice.NewNames))
        {
            var picture = Slot.Make(ChangeOptions.Icon(id), 32);
            picture.MouseFilter = Control.MouseFilterEnum.Stop;
            picture.TooltipText = name;
            parent.AddChild(picture);
        }
        if (set.Stance != null)
        {
            var stance = Ui.Add(parent, Ui.Label(PanelState.Names()(set.Stance), TextStyle.Tiny));
            stance.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        }
    }
}
