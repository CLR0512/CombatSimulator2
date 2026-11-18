using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// A master's editor under its row on the Slayer page: the side's block per open slot, a saved loadout per monster,
/// and the actions on that master alone. Every choice is a change on the side shown; nothing changes the game.
/// </summary>
/// <param name="letter">The side shown.</param>
/// <param name="side">Its state.</param>
/// <param name="usable">The character's unlocked loadout slots, in the game's order.</param>
/// <param name="loadoutNames">Every loadout's name by slot.</param>
/// <param name="monsterArt">A monster's picture by enemy id.</param>
/// <param name="findBest">Starts Find best on the masters given.</param>
internal sealed class MasterEditor(
    SideLetter letter,
    SideState side,
    IReadOnlyList<string> usable,
    IReadOnlyDictionary<string, string> loadoutNames,
    Func<string, Texture2D?> monsterArt,
    Action<IReadOnlyCollection<string>> findBest
)
{
    // The side's letter, drawn gold.
    private string Letter => SideLine.Of(letter == SideLetter.A ? "A" : "B");

    /// <summary>The editor for <paramref name="master"/>, its blocks in the save given, its best when found.</summary>
    public PanelContainer Make(
        MasterSpec master,
        Texture2D? coin,
        IReadOnlyList<string> savedBlocked,
        BestMaster? best,
        Func<string, string> names
    )
    {
        var id = master.Master;
        var box = Look.Panel(Art.RowBox, Ui.Id("Cs2MasterEditor", Ids.Brief(id)));
        var column = Ui.Column(10);
        box.AddChild(Ui.Margin(column, 20, 10, 20, 12));

        // Blocks: one picker per open slot, the master's monsters to choose from; a closed slot says what opens it.
        var blocksHead = Ui.Add(column, Ui.Row(12));
        // Translators: Heading in a master's editor on the Slayer page; {0} is the side, A or B.
        blocksHead.AddChild(Marked(Tr.Format("Blocks on {0}", Letter), TextStyle.BodyStrong));
        var open = master.Facts.Slots.Count(s => s.Open);
        if (master.Facts.BlockCost > 0)
        {
            var cost = Ui.Add(blocksHead, Ui.Row(4));
            cost.AddChild(Ui.Label(Tr.Plural("{0} slot open ·", "{0} slots open ·", open, open), TextStyle.Small));
            if (coin != null)
                cost.AddChild(Ui.Picture(coin, 14));
            cost.AddChild(Ui.Label(Tr.Format("{0} each", Ui.Number(master.Facts.BlockCost)), TextStyle.Small));
        }
        var slots = Ui.Add(column, new HFlowContainer());
        slots.AddThemeConstantOverride("h_separation", 12);
        slots.AddThemeConstantOverride("v_separation", 6);
        // Blocks and choices by enemy id: the pool's keys carry the master, the blocks' don't.
        var blocked = SlotOrder(id, [.. master.Blocked.Select(EnemyOf)]);
        var choices = master.Pool.Select(t => t.Enemy ?? t.Key).Distinct().ToList();
        var index = 0;
        foreach (var slot in master.Facts.Slots)
        {
            var number = ++index;
            var cell = Ui.Add(slots, Ui.Row(6));
            cell.AddChild(Ui.Label(Tr.Format("Slot {0}", number), TextStyle.Small));
            if (!slot.Open)
            {
                cell.AddChild(
                    Ui.Label(
                        slot.TasksNeeded > 0
                            ? "🔒 " + Tr.Plural("{0} more task", "{0} more tasks", slot.TasksNeeded, slot.TasksNeeded)
                            : "🔒",
                        TextStyle.Small
                    )
                );
                continue;
            }
            var current = number - 1 < blocked.Count && blocked[number - 1].Length > 0 ? blocked[number - 1] : null;
            // As the page's goal list and the results' metric list: each monster with its picture.
            var picker = new Dropdown(Ui.Id("Cs2MasterBlockSlot", number.ToString(Tr.Culture)), Dropdown.Plain)
            {
                Ends = 3,
            };
            picker.FixWidth(220);
            picker.AddItem(Tr.Get("None", "block"));
            foreach (var choice in choices)
                picker.AddItem(names(choice), monsterArt(choice));
            picker.Select(current == null ? 0 : choices.IndexOf(current) + 1);
            var at = number - 1;
            picker.ItemSelected += i =>
            {
                var next = blocked.ToList();
                while (next.Count <= at)
                    next.Add("");
                var picked = i == 0 ? "" : choices[i - 1];
                // A monster picked here leaves the slot it was in, as the game's blocks hold each monster once.
                for (var k = 0; k < next.Count; k++)
                    if (k != at && next[k] == picked)
                        next[k] = "";
                next[at] = picked;
                _slotOrder[id] = next;
                SetBlocks(id, [.. next.Where(k => k.Length > 0).Distinct()], [.. savedBlocked.Select(EnemyOf)]);
            };
            cell.AddChild(picker.Root);
        }

        // Loadouts: a picker per monster the side doesn't block; the setup unless one is chosen.
        var loadoutsHead = Ui.Add(column, Ui.Row(12));
        loadoutsHead.AddChild(Marked(Tr.Format("Loadouts on {0}", Letter), TextStyle.BodyStrong));
        loadoutsHead.AddChild(
            Ui.Label(Tr.Get("a saved loadout per monster; the rest fight with the setup"), TextStyle.Small)
        );
        if (usable.Count == 0)
            column.AddChild(
                Ui.Label(
                    Tr.Get("No saved loadouts yet. Save loadouts in the game to try one per monster."),
                    TextStyle.Small,
                    wrap: true
                )
            );
        else
        {
            var grid = Ui.Add(column, new HFlowContainer());
            grid.AddThemeConstantOverride("h_separation", 24);
            grid.AddThemeConstantOverride("v_separation", 6);
            var pairs =
                side.Changes.Find(new ChangeKey(ChangeKind.Loadouts, id))
                    ?.Loadouts.ToDictionary(p => p.Enemy, p => p.Slot, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var enemy in choices.Where(e => !blocked.Contains(e)))
            {
                var cell = Ui.Add(grid, Ui.Row(8));
                cell.AddChild(Slot.Make(monsterArt(enemy), 30));
                var name = Ui.Clipped(names(enemy), TextStyle.Small);
                name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                name.CustomMinimumSize = new Vector2(130, 0);
                name.SizeFlagsHorizontal = Control.SizeFlags.Fill;
                cell.AddChild(name);
                var slotsOffered = usable.ToList();
                var current = pairs.GetValueOrDefault(enemy);
                if (current != null && !slotsOffered.Contains(current))
                    slotsOffered.Add(current);
                // Its list opens from its right edge, under the arrow.
                var picker = new Dropdown(Ui.Id("Cs2MasterLoadoutPicker", Ids.Brief(enemy)), Dropdown.Link)
                {
                    RightAligned = true,
                };
                picker.AddItem(Tr.Get("Same as setup"));
                foreach (var slot in slotsOffered)
                    picker.AddItem(
                        usable.Contains(slot)
                            ? loadoutNames.GetValueOrDefault(slot) ?? names(slot)
                            : Tr.Format("{0} · gone", names(slot))
                    );
                picker.Select(current == null ? 0 : slotsOffered.IndexOf(current) + 1);
                picker.ItemSelected += i =>
                {
                    var next = new Dictionary<string, string>(pairs, StringComparer.Ordinal);
                    if (i == 0)
                        next.Remove(enemy);
                    else
                        next[enemy] = slotsOffered[i - 1];
                    SetLoadouts(id, next);
                };
                cell.AddChild(picker.Root);
            }
        }

        // The master's own actions.
        var actions = Ui.Add(column, Ui.Row(8));
        var service = RunService.Instance;
        // Translators: Button in a master's editor: Find best on this master alone (the editor is under its row).
        var find = Look.SmallAction(
            Ui.Button(Tr.Get("Find best"), Look.Button4, "Cs2MasterFindBest", () => findBest([Ids.MasterKey(id)]))
        );
        find.Disabled = service.IsActive;
        actions.AddChild(find);
        if (best is { Figure: not null })
        {
            var use = Ui.Button("", Look.Button4, "Cs2MasterUseBest", () => Slayer.MonsterLoadouts.Use(best));
            use.Disabled = Slayer.MonsterLoadouts.IsOnB(best);
            SideLine.OnButton(use, Tr.Format("Use best on {0}", SideLine.B));
            actions.AddChild(use);
        }
        // Translators: Button in a master's editor: drop this side's blocks and loadouts for this master; {0} is A or B.
        var clear = Ui.Button(
            "",
            Look.Button4,
            "Cs2MasterClear",
            () =>
                PanelState.SetSide(
                    letter,
                    side.WithChanges(
                        side.Changes.Without(new ChangeKey(ChangeKind.Loadouts, id))
                            .Without(new ChangeKey(ChangeKind.Blocks, id))
                    )
                )
        );
        clear.Disabled =
            side.Changes.Find(new ChangeKey(ChangeKind.Loadouts, id)) == null
            && side.Changes.Find(new ChangeKey(ChangeKind.Blocks, id)) == null;
        SideLine.OnButton(clear, Tr.Format("Clear on {0}", Letter));
        actions.AddChild(clear);
        return box;
    }

    // Which slot each master's blocked monster was picked in, empty slots as "": the side keeps blocks as a set, and
    // the game and the simulation don't mind which slot holds which, so only the editor remembers, for the session.
    private static readonly Dictionary<string, List<string>> _slotOrder = new(StringComparer.Ordinal);

    // The blocks in the slots they were picked in while the set is the one picked; else in the side's order.
    private static List<string> SlotOrder(string master, List<string> blocked) =>
        _slotOrder.TryGetValue(master, out var order)
        && order
            .Where(e => e.Length > 0)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(blocked.Order(StringComparer.Ordinal))
            ? order
            : blocked;

    private static string EnemyOf(string key) => TargetSpec.FromKey(key).Enemy ?? key;

    // A label in the side line's marked text: its letters gold.
    private static Label Marked(string text, TextStyle style)
    {
        var label = Ui.Label("", style);
        SideLine.OnLabel(label, text, style);
        return label;
    }

    // The side's blocks for a master, by enemy id: no change when they are the save's own.
    private void SetBlocks(string master, IReadOnlyList<string> enemies, IReadOnlyList<string> saved)
    {
        var key = new ChangeKey(ChangeKind.Blocks, master);
        var changes = enemies.Order(StringComparer.Ordinal).SequenceEqual(saved.Order(StringComparer.Ordinal))
            ? side.Changes.Without(key)
            : side.Changes.With(
                new Change(ChangeKind.Blocks, master, enemies.Count == 0 ? Change.None : string.Join(',', enemies))
            );
        PanelState.SetSide(letter, side.WithChanges(changes));
    }

    private void SetLoadouts(string master, Dictionary<string, string> pairs)
    {
        var key = new ChangeKey(ChangeKind.Loadouts, master);
        var changes =
            pairs.Count == 0
                ? side.Changes.Without(key)
                : side.Changes.With(
                    new Change(ChangeKind.Loadouts, master, string.Join(',', pairs.Select(p => p.Key + "@" + p.Value)))
                );
        PanelState.SetSide(letter, side.WithChanges(changes));
    }
}
