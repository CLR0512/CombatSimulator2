using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A section with a title that opens and closes it, a summary on the right, and a body.</summary>
internal sealed class Disclosure
{
    private readonly Button _chevron;
    private readonly VBoxContainer _body;
    private readonly Label _summary;

    public Disclosure(string name, string title, string summary, bool open, Action<bool> toggled)
    {
        Root = Ui.Column(6, name);
        // Title and summary share the line and shorten with "…" rather than widen the column at large text.
        var head = Ui.Add(Root, Ui.Row(6, name + "Head"));
        _chevron = Ui.Button(open ? "▾" : "▸", "", name + "Toggle", null);
        _chevron.Flat = true;
        _chevron.CustomMinimumSize = new Vector2(18, 0);
        head.AddChild(_chevron);
        var label = Ui.Clipped(title, TextStyle.Heading);
        label.SizeFlagsStretchRatio = 2;
        label.MouseFilter = Control.MouseFilterEnum.Stop;
        head.AddChild(label);
        _summary = Ui.Clipped(summary, TextStyle.Tiny);
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        _summary.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_summary);
        _body = Ui.Add(Root, Ui.Column(6, name + "Body"));
        _body.Visible = open;
        void Toggle()
        {
            _body.Visible = !_body.Visible;
            _chevron.Text = _body.Visible ? "▾" : "▸";
            toggled(_body.Visible);
        }
        _chevron.Pressed += Toggle;
        label.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                Toggle();
        };
    }

    public VBoxContainer Root { get; }

    public VBoxContainer Body => _body;

    public bool Open => _body.Visible;

    public string Summary
    {
        get => _summary.Text;
        set => Ui.SetClipped(_summary, value);
    }

    /// <summary>Points the chevron to match the body after code opened or closed it.</summary>
    public void Sync() => _chevron.Text = _body.Visible ? "▾" : "▸";
}
