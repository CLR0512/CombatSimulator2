using Godot;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>Best · Cheaper · Safer as plain words, the one shown underlined: a light row, not another row of buttons.</summary>
internal static class AnswerTabs
{
    /// <summary>The answer's tabs, <paramref name="tab"/> the one shown; null with no alternative.</summary>
    public static HBoxContainer? Text(Answer answer, string tab, Action<string> chosen)
    {
        if (SetupRows.Tabs(answer) is not { } tabs)
            return null;
        var row = Ui.Row(14, "GearSearchTabs");
        foreach (var (id, words, hover) in tabs)
        {
            var on = id == tab;
            var word = Ui.Button(words, "", Ui.Id("GearSearchTab", id), () => chosen(id));
            word.TooltipText = hover;
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
                word.AddThemeStyleboxOverride(state, Line(on));
            word.AddThemeColorOverride("font_color", on ? Colors.White : Colors.White with { A = 0.6f });
            word.AddThemeColorOverride("font_hover_color", Colors.White);
            row.AddChild(word);
        }
        return row;
    }

    private static StyleBoxFlat Line(bool on) =>
        new()
        {
            BgColor = Colors.Transparent,
            BorderColor = on ? Colors.White : Colors.Transparent,
            BorderWidthBottom = 2,
            ContentMarginBottom = 2,
        };
}
