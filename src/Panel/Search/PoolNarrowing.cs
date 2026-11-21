using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// With every item, what narrows it, at the top of the start form: only what the character's levels meet, only
/// content it has reached, and the sources left out as chips whose ✕ brings one back. A source is left out from an
/// answer's "Items to get". All three are remembered.
/// </summary>
internal sealed class PoolNarrowing
{
    private readonly Button _wearable;
    private readonly Button _reached;
    private readonly HBoxContainer _leftRow;
    private readonly HFlowContainer _chips;
    private readonly Action _changed;

    /// <summary>The choices; <paramref name="changed"/> hears of each change, to count the search again.</summary>
    public PoolNarrowing(Action changed)
    {
        _changed = changed;
        Root = Ui.Column(6, "GearSearchPoolNarrowing");
        _wearable = Cs2Check.Make(
            "GearSearchPoolWearable",
            // Translators: Tick under "Find best setup … from every item": only items the character's levels already meet.
            Tr.Get("Wearable at my levels"),
            false,
            on =>
            {
                PanelState.Prefs.SearchPoolWearable = on;
                Saved();
            }
        );
        _wearable.TooltipText = Tr.Get("Only items whose requirements your levels already meet.");
        Root.AddChild(_wearable);
        _reached = Cs2Check.Make(
            "GearSearchPoolReached",
            // Translators: Tick under "Find best setup … from every item": only items owned or got from content the character has reached.
            Tr.Get("Content I've reached"),
            false,
            on =>
            {
                PanelState.Prefs.SearchPoolReached = on;
                Saved();
            }
        );
        _reached.TooltipText = Tr.Get(
            "Only items you own, or can get from monsters, dungeons, shops and skills you have reached."
        );
        Root.AddChild(_reached);
        _leftRow = Ui.Add(Root, Ui.Row(8, "GearSearchLeftOut"));
        // Translators: Label of the item sources a search over every item leaves out, shown as chips; also the link beside an answer's item to get that leaves its source out.
        var label = Ui.Add(_leftRow, Ui.Label(Tr.Get("Leave out"), TextStyle.Label));
        label.CustomMinimumSize = new Vector2(76, 0);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        _chips = Ui.Add(_leftRow, new HFlowContainer { Name = "GearSearchLeftOutList" });
        _chips.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _chips.AddThemeConstantOverride("h_separation", 6);
        _chips.AddThemeConstantOverride("v_separation", 4);
    }

    public VBoxContainer Root { get; }

    /// <summary>The choices as saved; shown only when the search chooses from every item.</summary>
    public void Show(bool every)
    {
        Root.Visible = every;
        if (!every)
            return;
        var prefs = PanelState.Prefs;
        Cs2Check.Set(_wearable, prefs.SearchPoolWearable);
        Cs2Check.Set(_reached, prefs.SearchPoolReached);
        Ui.Clear(_chips);
        foreach (var source in prefs.SearchLeftOut)
        {
            var chip = FramedButton.Make(
                Ui.Button(
                    Spoilers.Current.Name(source) + "  ✕",
                    // Translators: Hover of a left-out source's chip in the start form.
                    Tr.Get("Bring it back"),
                    Ui.Id("GearSearchLeftOutChip", source),
                    () =>
                    {
                        PanelState.Prefs.SearchLeftOut = [.. PanelState.Prefs.SearchLeftOut.Where(s => s != source)];
                        Saved();
                        Show(true);
                    }
                ),
                6,
                2
            );
            chip.AddThemeFontSizeOverride("font_size", Look.Style(TextStyle.Small).FontSize);
            _chips.AddChild(chip);
        }
        _leftRow.Visible = prefs.SearchLeftOut.Count > 0;
    }

    private void Saved()
    {
        PanelState.Save();
        _changed();
    }
}
