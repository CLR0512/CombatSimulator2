using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>What a page's marks mean, each shown as a row draws it: the grey tick, the green tick, the changed row's box.</summary>
internal static class PageLegend
{
    public static HBoxContainer Make(StyleBox changedBox)
    {
        var legend = Ui.Row(8, "Cs2PageLegend");
        legend.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        legend.AddChild(Mark(owned: true));
        legend.AddChild(Word(Tr.Get("owned")));
        legend.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
        legend.AddChild(Mark(owned: false));
        legend.AddChild(Word(Tr.Get("on")));
        legend.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
        var changed = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        PixelBox.Panel(changed, changedBox);
        changed.AddChild(Word(Tr.Get("changed")));
        legend.AddChild(changed);
        return legend;

        static Button Mark(bool owned)
        {
            var tick = Cs2Check.Make("", on: true);
            tick.MouseFilter = Control.MouseFilterEnum.Ignore;
            tick.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            Cs2Check.SetOwned(tick, owned);
            return tick;
        }

        static Label Word(string text)
        {
            var word = Ui.Label(text, TextStyle.Tiny);
            word.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            return word;
        }
    }
}
