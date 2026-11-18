using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

// The levels column: each combat skill with steps down and up, capped as the game caps it.
internal sealed partial class GearPage
{
    private void Levels(SideView view)
    {
        _levels.AddChild(Ui.Label(Tr.Get("Levels"), TextStyle.Label));
        foreach (var skill in view.Base.Levels)
        {
            var level = view.Level(skill.Skill);
            var changed = level != skill.Level;
            var brief = Ids.Brief(skill.Skill);
            var row = Ui.Add(_levels, Ui.Row(6, "Cs2Level" + brief));
            row.AddChild(Ui.Picture(ChangeOptions.Icon(skill.Skill), 22));
            var name = Ui.Label(Name(skill.Skill), changed ? TextStyle.BodyStrong : TextStyle.Body);
            name.CustomMinimumSize = new Vector2(96, 0);
            row.AddChild(name);
            var was = Ui.Label(changed ? Tr.Format("was {0}", skill.Level) : "", TextStyle.Tiny);
            was.CustomMinimumSize = new Vector2(52, 0);
            row.AddChild(was);
            var lowest = Changes.LowestLevel(skill.Skill);
            row.AddChild(Step(brief + "-", "−", () => SetLevel(view, skill, level - 1), level > lowest));
            var value = Ui.Label(level.ToString(Tr.Culture), TextStyle.BodyStrong, brief + "Value");
            value.HorizontalAlignment = HorizontalAlignment.Center;
            value.CustomMinimumSize = new Vector2(40, 0);
            if (changed)
                value.Modulate = Look.Safe;
            row.AddChild(value);
            row.AddChild(Step(brief + "+", "+", () => SetLevel(view, skill, level + 1), level < skill.Cap));
            row.AddChild(
                Step(brief + "Max", Tr.Get("max"), () => SetLevel(view, skill, skill.Cap), level != skill.Cap)
            );
        }
        var cap = view.Base.Levels.Count > 0 ? view.Base.Levels.Max(l => l.Cap) : 0;
        _levels.AddChild(
            Ui.Label(Tr.Format("Max {0} in this build. Hitpoints at least 10.", cap), TextStyle.Tiny, wrap: true)
        );
    }

    private Button Step(string name, string text, Action pressed, bool enabled)
    {
        var button = Ui.Button(text, Look.Button4, "Cs2Level" + name, pressed);
        button.Disabled = _readOnly || !enabled;
        return button;
    }

    private void SetLevel(SideView view, SkillLevel skill, int level)
    {
        level = Math.Clamp(level, Changes.LowestLevel(skill.Skill), Math.Max(skill.Cap, skill.Level));
        var change = new Change(ChangeKind.Level, skill.Skill, level.ToString(CultureInfo.InvariantCulture));
        Edit(view, change, level == skill.Level);
    }
}
