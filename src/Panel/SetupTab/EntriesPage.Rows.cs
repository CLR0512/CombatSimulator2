using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Stats;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

// A row of the list, made once and filled for whichever heading or entry is in view: icon, name, its note (what it
// needs, why it is fixed), its control, and "was".
internal sealed partial class EntriesPage
{
    private PanelContainer MakeRow()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        var line = Ui.Add(panel, Ui.Row(8));
        line.Name = "Line";
        var icon = Ui.Picture(null, 28, "Icon");
        icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(icon);
        var name = Ui.Clipped("", TextStyle.Body, "Name");
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(name);
        var note = Ui.Clipped("", TextStyle.Tiny, "Note");
        note.CustomMinimumSize = new Vector2(240, 0);
        note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(note);
        // What a row sets, in a block of one width at its end: the name and the note before it then start at the
        // same place in every row, whatever the block holds.
        var right = Ui.Row(8, "Right");
        right.CustomMinimumSize = new Vector2(350, 0);
        right.Alignment = BoxContainer.AlignmentMode.End;
        // Boxed, so it reads as something to hover and not as part of the row's text.
        var modsBox = new PanelContainer { Name = "ModsBox", MouseFilter = Control.MouseFilterEnum.Ignore };
        PixelBox.Panel(modsBox, ModsBox());
        modsBox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var mods = Ui.Label("", TextStyle.Tiny, "Mods");
        mods.HorizontalAlignment = HorizontalAlignment.Center;
        mods.MouseFilter = Control.MouseFilterEnum.Stop;
        modsBox.AddChild(mods);
        right.AddChild(modsBox);
        var tick = Cs2Check.Make("Tick");
        tick.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        right.AddChild(tick);
        var down = Ui.Button("−", Look.Button4, "Down");
        right.AddChild(down);
        var value = Ui.Label("", TextStyle.BodyStrong, "Value");
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.CustomMinimumSize = new Vector2(64, 0);
        value.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        right.AddChild(value);
        var up = Ui.Button("+", Look.Button4, "Up");
        right.AddChild(up);
        var was = Ui.Label("", TextStyle.Tiny, "Was");
        // Room for "was 100" before any row has one, so the steps before it never move.
        was.CustomMinimumSize = new Vector2(72, 0);
        was.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        right.AddChild(was);
        line.AddChild(right);
        tick.Toggled += on =>
        {
            if (!Binding && Entry(panel) is { } entry)
                Set(entry, on ? 1 : 0);
        };
        down.Pressed += () =>
        {
            if (Entry(panel) is { } entry)
                Set(entry, Value(entry) - 1);
        };
        up.Pressed += () =>
        {
            if (Entry(panel) is { } entry)
                Set(entry, Value(entry) + 1);
        };
        return panel;
    }

    // Set while a row is filled, so ticking its box to the side's value isn't taken as the player's click.
    private bool Binding { get; set; }

    private PageEntry? Entry(Control row) =>
        row.GetMeta("cs2_row", -1).AsInt32() is var i and >= 0 && i < _rows.Count ? _rows[i].Entry : null;

    private void BindRow(Control control, int index)
    {
        var panel = (PanelContainer)control;
        panel.SetMeta("cs2_row", index);
        var row = _rows[index];
        var line = panel.GetNode<HBoxContainer>("Line");
        var icon = line.GetNode<TextureRect>("Icon");
        var name = line.GetNode<Label>("Name");
        var note = line.GetNode<Label>("Note");
        var right = line.GetNode<HBoxContainer>("Right");
        var modsBox = right.GetNode<PanelContainer>("ModsBox");
        var mods = modsBox.GetNode<Label>("Mods");
        var tick = right.GetNode<Button>("Tick");
        var down = right.GetNode<Button>("Down");
        var value = right.GetNode<Label>("Value");
        var up = right.GetNode<Button>("Up");
        var was = right.GetNode<Label>("Was");
        var at = index.ToString(CultureInfo.InvariantCulture);
        Binding = true;
        if (row.Entry is not { } entry)
        {
            // A heading: the group's name and what the side has of it.
            panel.Name = Ui.Id("Cs2Group", row.Group.Id);
            PixelBox.Panel(panel, new StyleBoxEmpty());
            panel.ZIndex = 0;
            icon.Texture = row.Group.Icon is { } groupIcon ? ChangeOptions.Icon(groupIcon) : null;
            var heading = row.Group.Heading is { } own ? Tr.Note(own) : row.Group.Name;
            Ui.SetClipped(name, heading);
            name.TooltipText = "";
            panel.TooltipText = row.Group.Summary is { } about ? heading + "\n" + Tr.Note(about) : heading;
            name.LabelSettings = Look.Style(TextStyle.Label);
            note.Text = row.Group.Summary is { } summary ? Tr.Note(summary) : "";
            // The summary is the last check's: faded while the side has changed something of this group since,
            // until the next one answers.
            var stale =
                _check == null
                || row.Group.Entries.Any(e =>
                    e.Change is { } key && _side.Changes.Find(key) != _check.Changes.Changes.Find(key)
                );
            note.Modulate = Colors.White with { A = stale ? 0.35f : 0.75f };
            modsBox.Visible = tick.Visible = down.Visible = up.Visible = false;
            value.Text = Count(row.Group);
            was.Text = "";
            Binding = false;
            return;
        }
        panel.Name = "Cs2Entry" + at;
        var now = Value(entry);
        // Only what the side can set is marked as changed: an entry that follows another has no change of its own.
        var changed = entry.Change != null && now != entry.SaveValue;
        PixelBox.Panel(panel, changed ? ChangedBox() : Look.Box(Art.RowBox));
        // The row below draws its texture's shadow over this one's edge: a changed row's gold edge goes on top.
        panel.ZIndex = changed ? 1 : 0;
        icon.Texture = entry.Icon is { } id ? ChangeOptions.Icon(id) : null;
        var hidden = EntryShown.Hidden(_page!, entry);
        icon.Modulate = hidden ? Colors.Black : Colors.White;
        // A stand-in picture has no shape to darken.
        if (hidden && ChangeOptions.IsStandIn(icon.Texture))
            icon.Texture = null;
        var title =
            hidden ? Spoilers.Mask
            : entry.Names is { } names && now >= 0 && now < names.Count ? names[now]
            : entry.Name;
        Ui.SetClipped(name, title);
        // On the row, so it shows wherever in the row the pointer is.
        name.TooltipText = "";
        panel.TooltipText = entry.Description is { } description && !hidden ? title + "\n" + description : title;
        name.LabelSettings = Look.Style(TextStyle.Body);
        note.Text =
            hidden ? (entry.Change != null ? Tr.Get("Turn off Spoiler guard to try it") : "")
            : entry.Fixed is { } fixedWhy ? Tr.Note(fixedWhy)
            : now == 0 && _page!.Unmet(entry, Value) is { Count: > 0 } needs ? "🔒 " + Tr.Notes(needs, "; ")
            : entry.Remark is { } remark ? Tr.Note(remark)
            : "";
        note.TooltipText = note.Text;
        note.Modulate = Colors.White with { A = 0.75f };
        modsBox.Visible = entry.Modifiers.Count > 0 && !hidden;
        mods.Text = Tr.Plural("{0} modifier", "{0} modifiers", entry.Modifiers.Count, entry.Modifiers.Count);
        mods.TooltipText = string.Join("\n", entry.Modifiers.Select(FullStatsView.LineText));
        var editable = !_readOnly && entry.Change != null && entry.Fixed == null && !hidden;
        var toggle = entry.Kind == EntryKind.Toggle;
        tick.Visible = toggle;
        // On from off; off only above what the character has (a star it owns stays).
        tick.Disabled = !editable || (now > 0 && now <= Lowest(entry));
        var owned = editable && now > 0 && now <= Lowest(entry);
        tick.TooltipText = owned
            ? Tr.Get("The character has this: CS2 can add purchases and stars, not take them away.")
            : "";
        Cs2Check.SetOwned(tick, owned);
        Cs2Check.Set(tick, now > 0);
        var counted = entry.Kind is EntryKind.Count or EntryKind.Rank;
        down.Visible = up.Visible = counted && editable;
        down.Disabled = now <= Lowest(entry);
        up.Disabled = now >= entry.Max;
        value.Text =
            toggle ? ""
            : entry.Max > 1 ? Tr.Format("{0} of {1}", [now, entry.Max])
            : now.ToString(Tr.Culture);
        was.Text = changed ? Tr.Format("was {0}", entry.SaveValue) : "";
        Binding = false;
    }

    // A warm grey wash with a thin edge of the same, both see-through.
    private static StyleBoxFlat ModsBox()
    {
        var tone = Color.FromHtml("#cfc6b4");
        var box = new StyleBoxFlat
        {
            BgColor = tone with { A = 0.12f },
            BorderColor = tone with { A = 0.45f },
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(3);
        return box;
    }

    // Changed for this side: a green wash with a gold edge, the legend's "changed". Its inner room is the plain row's,
    // so a row's contents stay put when it changes.
    private static StyleBoxFlat ChangedBox()
    {
        var row = Look.Box(Art.RowBox);
        var box = new StyleBoxFlat
        {
            BgColor = Look.Safe with { A = 0.16f },
            BorderColor = Look.Best,
            ContentMarginLeft = row.GetContentMargin(Side.Left),
            ContentMarginRight = row.GetContentMargin(Side.Right),
            ContentMarginTop = row.GetContentMargin(Side.Top),
            ContentMarginBottom = row.GetContentMargin(Side.Bottom),
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(3);
        return box;
    }
}
