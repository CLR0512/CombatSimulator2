using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// A page the build has no data for (Agility, Herblore's potions, Firemaking's bonfires, Pets): that it isn't here,
/// what the page will do once the game has it, and that nothing from it counts in this build's results.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class MissingPage
{
    private readonly PanelContainer _root;
    private readonly Label _title;
    private readonly Label _will;
    private readonly Label _counts;

    public MissingPage()
    {
        _root = Look.Panel(Art.SideBox, "Cs2MissingPage");
        var column = Ui.Add(_root, Ui.Column(10));
        column.Alignment = BoxContainer.AlignmentMode.Center;
        column.CustomMinimumSize = new Vector2(0, 220);
        _title = Ui.Label("", TextStyle.Heading);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_title);
        _will = Ui.Label("", TextStyle.Body, wrap: true);
        _will.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_will);
        _counts = Ui.Label("", TextStyle.Small, wrap: true);
        _counts.HorizontalAlignment = HorizontalAlignment.Center;
        _counts.Modulate = Colors.White with { A = 0.75f };
        column.AddChild(_counts);
    }

    public Control Root => _root;

    public void Show(string page)
    {
        var name = PageList.Name(page);
        _title.Text = page switch
        {
            SetupPage.Herblore => Tr.Get("No combat potions in this build"),
            SetupPage.Firemaking => Tr.Get("No bonfires in this build"),
            _ => Tr.Format("{0} isn't in this build", name),
        };
        _will.Text = page switch
        {
            SetupPage.Agility => Tr.Get(
                "Once the game adds it, your courses and obstacles show here, and you can try others."
            ),
            SetupPage.Herblore => Tr.Get("Once the game adds combat potions, you can pick one for a side here."),
            SetupPage.Firemaking => Tr.Get("Once the game adds bonfires, you can light one for a side here."),
            _ => Tr.Get("Once the game adds pets, yours show here, and you can try others."),
        };
        _counts.Text = Tr.Format("Nothing from {0} affects results in this build.", name);
    }
}
