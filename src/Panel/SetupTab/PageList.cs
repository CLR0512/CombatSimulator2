using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// The Setup tab's pages under Fighting, Skills and Account, each with how many of the side's changes it holds, or
/// why it is greyed (there is nothing of it yet). The page shown is pressed.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class PageList
{
    private const float IconSize = 26;

    private readonly VBoxContainer _root;
    private readonly Dictionary<string, (Button Button, Label Info)> _rows = new(StringComparer.Ordinal);
    private readonly ButtonGroup _group = new();

    public PageList()
    {
        _root = Ui.Column(4, "Cs2PageList");
        PageSection? section = null;
        foreach (var (id, inSection) in SetupPage.All)
        {
            if (section != inSection)
            {
                section = inSection;
                // In line with the rows' icons, clear of the column's edge.
                _root.AddChild(Ui.Margin(Ui.Label(SectionName(inSection), TextStyle.Tiny), 8, 0, 0, 0));
            }
            _root.AddChild(Row(id));
        }
    }

    public Control Root => _root;

    /// <summary>Raised with the page the player picked.</summary>
    public event Action<string>? Picked;

    /// <summary>The page's name as the list and the page's title give it.</summary>
    public static string Name(string page) =>
        page switch
        {
            SetupPage.Gear => Tr.Get("Gear and levels"),
            SetupPage.Astrology => Tr.Get("Astrology"),
            SetupPage.Guild => Tr.Get("Mastery Guild"),
            SetupPage.Slayer => Tr.Get("Slayer"),
            SetupPage.Agility => Tr.Get("Agility"),
            SetupPage.Herblore => Tr.Get("Herblore"),
            SetupPage.Firemaking => Tr.Get("Firemaking"),
            SetupPage.Shop => Tr.Get("Shop upgrades"),
            SetupPage.Quests => Tr.Get("Quests"),
            _ => Tr.Get("Pets"),
        };

    private static string SectionName(PageSection section) =>
        section switch
        {
            PageSection.Character => Tr.Get("Character"),
            PageSection.Skills => Tr.Get("Skills"),
            _ => Tr.Get("Account"),
        };

    private static Texture2D? Icon(string page) =>
        page switch
        {
            SetupPage.Gear => Art.File(Art.CombatPath),
            SetupPage.Guild => Art.File(Art.MasteryPath),
            SetupPage.Shop => Art.File(Art.ShopPath),
            SetupPage.Quests => Art.File(Art.QuestsPath),
            SetupPage.Pets => ChangeOptions.PetsIcon(),
            _ => ChangeOptions.Icon("MelvorBase:" + char.ToUpperInvariant(page[0]) + page[1..]),
        };

    // A row: the page's icon, its name, and on the right its changes or why it's greyed.
    private Button Row(string page)
    {
        var button = Ui.Button("", Look.Button8, Ui.Id("Cs2Page", page), () => Picked?.Invoke(page));
        button.ToggleMode = true;
        button.ButtonGroup = _group;
        button.CustomMinimumSize = new Vector2(0, IconSize + 12);
        var line = Ui.Add(button, Ui.Row(8));
        line.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        line.OffsetLeft = 8;
        line.OffsetRight = -8;
        line.MouseFilter = Control.MouseFilterEnum.Ignore;
        var icon = Ui.Picture(Icon(page), IconSize);
        icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(icon);
        var name = Ui.Label(Name(page), TextStyle.Body);
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(name);
        // The note takes the room the name leaves and is cut short there, never drawn past the row's edge; the
        // row's tooltip has it whole.
        var info = Ui.Label("", TextStyle.Tiny);
        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        info.HorizontalAlignment = HorizontalAlignment.Right;
        info.ClipText = true;
        info.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        line.AddChild(info);
        _rows[page] = (button, info);
        return button;
    }

    /// <summary>
    /// Shows <paramref name="shown"/> pressed and each page's changes in <paramref name="changes"/>; a page in
    /// <paramref name="pages"/> the build has no data for is greyed with its reason, and opens on what it will do.
    /// </summary>
    public void Show(string shown, ChangeList changes, IReadOnlyList<SetupPage>? pages)
    {
        foreach (var (page, (button, info)) in _rows)
        {
            button.SetPressedNoSignal(page == shown);
            var setup = pages?.FirstOrDefault(p => p.Id == page);
            var count = SetupPage.ChangeCount(page, changes);
            var greyed = setup is { State: PageState.NoData };
            info.Text =
                greyed && setup!.Reason is { } reason ? Tr.Note(reason)
                : count > 0 ? Tr.Plural("{0} change", "{0} changes", count, count)
                : "";
            button.TooltipText = greyed ? info.Text : "";
            // Green marks a page with changes, except on the pressed row, which is green itself.
            info.Modulate =
                count > 0 && !greyed && page != shown ? Look.Safe
                : page == shown ? Colors.White
                : Colors.White with { A = 0.7f };

            button.Modulate = Colors.White with { A = greyed && page != shown ? 0.55f : 1f };
        }
    }
}
