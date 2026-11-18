using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A slot: an inset panel with a light border around a portrait or icon.</summary>
internal static class Slot
{
    public static PanelContainer Make(Texture2D? texture, float size, string name = "")
    {
        var slot = new PanelContainer
        {
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (name.Length > 0)
            slot.Name = name;
        PixelBox.Panel(slot, Look.Box(Look.SlotBox));
        slot.AddChild(Ui.Picture(texture, size - 6));
        return slot;
    }

    public static void Set(PanelContainer slot, Texture2D? texture) =>
        ((TextureRect)slot.GetChild(0)).Texture = texture;

    /// <summary>A slot with a quantity under the icon, as in loot rows.</summary>
    public static VBoxContainer WithCount(Texture2D? texture, string count, float size, string tooltip)
    {
        var column = Ui.Column(2);
        column.TooltipText = tooltip;
        column.MouseFilter = Control.MouseFilterEnum.Pass;
        column.AddChild(Make(texture, size));
        var label = Ui.Label(count, TextStyle.Tiny);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(label);
        return column;
    }
}
