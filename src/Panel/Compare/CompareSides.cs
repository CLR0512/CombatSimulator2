using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>
/// The comparison's two sides, above everything that depends on them: a card per side with its letter, what it
/// changed first (with the item's icon) and how many more, every change in its tooltip; then the changes a side was
/// refused, with why.
/// </summary>
internal sealed class CompareSides
{
    private readonly HBoxContainer _cards;
    private readonly Label _seed;
    private readonly VBoxContainer _leftOut;

    public CompareSides()
    {
        Root = Ui.Column(6, "Cs2CompareSides");
        _cards = Ui.Add(Root, Ui.Row(8, "Cs2CompareSideCards"));
        _seed = Ui.Add(Root, Ui.Label(Tr.Get("Same seed per target"), TextStyle.Tiny, "Cs2CompareSeed"));
        _leftOut = Ui.Add(Root, Ui.Column(2, "Cs2CompareLeftOut"));
    }

    public VBoxContainer Root { get; }

    /// <summary>Shows <paramref name="a"/> and <paramref name="b"/>; <paramref name="art"/> gives item icons.</summary>
    public void Show(Run a, Run b, bool saved, Func<string, Texture2D?>? art)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ShowCards(a, b, saved, art);
        ShowLeftOut(a.ChangeOutcomes, b.ChangeOutcomes);
    }

    // A side without changes shows its setup's name.
    private void ShowCards(Run a, Run b, bool saved, Func<string, Texture2D?>? art)
    {
        Ui.Clear(_cards);
        var names = PanelState.Names();
        foreach (var (letter, run, isSaved) in new[] { ("A", a, saved), ("B", b, false) })
        {
            var card = Look.Panel(Art.RowBox, "Cs2CompareSide" + letter);
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _cards.AddChild(card);
            var line = Ui.Add(card, Ui.Row(8));
            var mark = Ui.Add(line, Ui.Label(letter, TextStyle.BodyStrong));
            mark.LabelSettings = SideLine.Gold(TextStyle.BodyStrong);
            mark.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            var applied = run.ChangeOutcomes?.Where(o => o.Applies).ToList() ?? [];
            var first = applied.FirstOrDefault();
            if (first?.Change.Item is { } item && art != null)
                line.AddChild(Slot.Make(art("item:" + item), 28));
            var text = Ui.Add(line, Ui.Column(0));
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            string title;
            var under = new List<string>();
            if (first == null)
                title = isSaved ? Tr.Get("saved run") : Words.Setup(run.Setup);
            else if (first.Change.Item is { } worn)
            {
                title = names(worn);
                if (first.From is { } from && from != Change.None)
                    // Translators: Under a side's changed item in a comparison: {0} the item it replaced.
                    under.Add(Tr.Format("was {0}", names(from)));
            }
            else
                title = Tr.Note(ChangeNotes.Describe(first.Change, first.From, names));
            if (applied.Count > 1)
                // Translators: Under a side's first change in a comparison: {0} how many more it made.
                under.Add(Tr.Format("+{0} more", applied.Count - 1));
            text.AddChild(Ui.Label(title, TextStyle.Small, wrap: true));
            if (under.Count > 0)
                text.AddChild(Ui.Label(string.Join(" · ", under), TextStyle.Tiny, wrap: true));
            card.TooltipText = string.Join(
                "\n",
                applied.Select(o =>
                {
                    var change = Tr.Note(ChangeNotes.Describe(o.Change, o.From, names));
                    return o.State == ChangeState.RequirementIgnored
                        ? Tr.Format("{0} · requirement ignored", change)
                        : change;
                })
            );
        }
    }

    // The changes a side was refused, with why: a side ran without them.
    private void ShowLeftOut(IReadOnlyList<ChangeOutcome>? a, IReadOnlyList<ChangeOutcome>? b)
    {
        Ui.Clear(_leftOut);
        var names = PanelState.Names();
        foreach (var (title, outcomes) in new[] { (Tr.Get("Left out of A"), a), (Tr.Get("Left out of B"), b) })
        {
            var refused = outcomes?.Where(o => o.LeftOut).ToList() ?? [];
            if (refused.Count == 0)
                continue;
            _leftOut.AddChild(Ui.Label(title, TextStyle.Label));
            foreach (var outcome in refused)
            {
                _leftOut.AddChild(
                    Ui.Label(
                        Tr.Note(ChangeNotes.Describe(outcome.Change, outcome.From, names)),
                        TextStyle.Small,
                        wrap: true
                    )
                );
                var reason = Ui.Label(Tr.Notes(outcome.Reasons, "; "), TextStyle.Tiny, wrap: true);
                reason.Modulate = Look.Deadly;
                _leftOut.AddChild(reason);
            }
        }
        _leftOut.Visible = _leftOut.GetChildCount() > 0;
    }
}
