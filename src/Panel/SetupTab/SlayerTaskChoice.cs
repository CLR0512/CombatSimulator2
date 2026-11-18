using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// The Slayer page's "Slayer task" choice for the side shown: as the save, on task or off task, for every single monster
/// it runs. A change on the side like any other; masters are always on task.
/// </summary>
internal sealed class SlayerTaskChoice
{
    private const string AsSave = "save";
    private readonly Segmented _choice;
    private SideLetter _letter;
    private SideState _side = SideState.AsNow;

    public SlayerTaskChoice()
    {
        Root = Ui.Column(4, "Cs2SlayerTask");
        Root.AddChild(Ui.Label(Tr.Get("Slayer task"), TextStyle.Heading));
        // Translators: The Slayer page's choice of whether single monsters are fought on a slayer task.
        _choice = new Segmented(
            "Cs2SlayerTaskChoice",
            [
                (AsSave, Tr.Get("As save"), "Cs2SlayerTaskSave"),
                (Change.On, Tr.Get("On task"), "Cs2SlayerTaskOn"),
                (Change.Off, Tr.Get("Off task"), "Cs2SlayerTaskOff"),
            ],
            Choose,
            side: 6,
            ends: 3
        );
        Root.AddChild(_choice.Root);
        // What each choice does, on its button: the page keeps the line's room.
        _choice.Button(AsSave).TooltipText = Tr.Get(
            "Single monsters are fought on a task only when your save has one for them."
        );
        _choice.Button(Change.On).TooltipText = Tr.Get(
            "Each single monster is fought on a task from a master that gives it, with Slayer XP, coins and on-task bonuses. Masters are always on task."
        );
        _choice.Button(Change.Off).TooltipText = Tr.Get(
            "Single monsters are fought with no slayer task. Masters are always on task."
        );
    }

    public VBoxContainer Root { get; }

    public void Show(SideLetter letter, SideState side, bool readOnly)
    {
        _letter = letter;
        _side = side;
        var choice = side.Changes.Find(new ChangeKey(ChangeKind.SlayerTask, null))?.Value ?? AsSave;
        _choice.Show(choice);
        _choice.SetDisabled(readOnly);
    }

    private void Choose(string choice)
    {
        var key = new ChangeKey(ChangeKind.SlayerTask, null);
        var changes =
            choice == AsSave
                ? _side.Changes.Without(key)
                : _side.Changes.With(new Change(ChangeKind.SlayerTask, null, choice));
        PanelState.SetSide(_letter, _side.WithChanges(changes));
    }
}
