using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Slayer;

/// <summary>
/// A master's Monsters tab: its best setup when Find best has found one, with Use on B; its monsters with their share of
/// its time, rate and deaths; the blocks suggested from this run, with Use on B; and its risk. The side's blocks and
/// loadouts are changed on the Setup tab's Slayer page. Each section folds, its heading giving its answer. Nothing here
/// changes the game.
/// </summary>
internal static class MasterDetail
{
    // Sections opened or folded stay so from one master to the next, for the session.
    private static bool _poolOpen = true;
    private static bool _blocksOpen = true;
    private static bool _riskOpen;

    public static void Fill(VBoxContainer page, Run run, TargetResult master, string metric, TimeUnit unit)
    {
        Ui.Clear(page);
        var counted = MasterParts.Counted(master);
        var key = Metrics.Resolve(run, metric);
        var names = PanelState.Names();
        // The monster's own name: its area is the master's, said once above.
        string NameOf(string partKey) => names(TargetSpec.FromKey(Ids.PartTarget(partKey)).Enemy ?? partKey);
        if (MasterFigures.Stuck(counted) is { } stuck)
            page.AddChild(
                Ui.Label(
                    Tr.Format("Gets stuck on {0}: {1}", NameOf(stuck.Key), Tr.Note(stuck.Reason)),
                    TextStyle.Body,
                    "Cs2MasterStuck",
                    wrap: true
                )
            );
        if (MonsterLoadouts.Banner(master, names) is { } banner)
            page.AddChild(banner);
        if (BestLine(master) is { } best)
            page.AddChild(best);
        page.AddChild(Pool(master, counted, key, unit, NameOf, names).Root);
        page.AddChild(MonsterLoadouts.Section(master));
        if (RunCache.Blocks(run, master.Key, metric) is { } suggestion)
            page.AddChild(Blocks(master, suggestion, names).Root);
        if (MasterFigures.Risk(counted, unit) is { } risk)
            page.AddChild(Risk(risk, NameOf).Root);
    }

    private static Disclosure Pool(
        TargetResult master,
        List<TargetResult> counted,
        string key,
        TimeUnit unit,
        Func<string, string> nameOf,
        Func<string, string> names
    )
    {
        var shares = MasterFigures.Shares(counted).ToDictionary(s => s.Key, s => s.Share, StringComparer.Ordinal);
        var top = shares.Count > 0 ? shares.MaxBy(p => p.Value) : default;
        // Folded, the heading gives the monster that takes the most of the master's time.
        var summary =
            top.Key == null
                ? Tr.Plural("{0} monster", "{0} monsters", counted.Count, counted.Count)
                : Tr.Format("{0} {1}", nameOf(top.Key), Ui.Percent(top.Value));
        var section = new Disclosure("Cs2MasterPool", Tr.Get("Monsters"), summary, _poolOpen, open => _poolOpen = open);
        // Translators: Under "Monsters" in a Slayer master's details.
        section.Body.AddChild(
            Ui.Label(
                Tr.Get("Each comes up as often as any other, so the slow ones take most of the time."),
                TextStyle.Small,
                wrap: true
            )
        );
        var game = LiveGame.Find();
        var index = 0;
        foreach (var part in master.Parts ?? [])
        {
            var target = Ids.PartTarget(part.Key);
            var isBlocked = (master.Blocked ?? []).Contains(target);
            var box = Look.Panel(Art.RowBox, Ui.Id("Cs2MasterMonster", (index++).ToString(Tr.Culture)));
            var line = Ui.Add(box, Ui.Row(8));
            line.AddChild(Slot.Make(game != null ? Art.Target(game, TargetSpec.FromKey(target)) : Art.Missing, 32));
            var who = Ui.Add(line, Ui.Column(0));
            who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var named = Ui.Add(who, Ui.Row(6));
            named.AddChild(Ui.Clipped(nameOf(part.Key), TextStyle.Small));
            foreach (
                var mark in new Control?[]
                {
                    MonsterLoadouts.Tag(master, target, names),
                    MonsterLoadouts.Pick(master, target, names),
                }
            )
                if (mark != null)
                    named.AddChild(mark);
            var rate = Metrics.Estimate(part, key, unit, PanelState.Prices());
            var deaths = Metrics.Estimate(part, Metrics.Deaths, TimeUnit.Hour);
            who.AddChild(
                Ui.Clipped(
                    isBlocked ? Tr.Get("Blocked in this run")
                        : !part.HasEstimates ? Tr.Notes(part.Reasons, " · ")
                        : Tr.Format(
                            "{0} of the time · {1} {2} · {3} deaths / h",
                            [
                                Ui.Percent(shares.GetValueOrDefault(part.Key)),
                                Ui.Number(rate?.Point ?? 0),
                                Words.PerUnitShort(unit),
                                Ui.Number(deaths?.Point ?? 0),
                            ]
                        ),
                    TextStyle.Tiny
                )
            );
            box.Modulate = Colors.White with { A = isBlocked ? 0.55f : 1f };
            section.Body.AddChild(box);
        }
        return section;
    }

    private static Disclosure Blocks(TargetResult master, BlockSuggestion suggestion, Func<string, string> names)
    {
        string Named(IReadOnlyList<string> keys) =>
            keys.Count == 0
                ? Tr.Get("none")
                : string.Join(", ", keys.Select(k => names(TargetSpec.FromKey(k).Enemy ?? k)));
        var best = suggestion.Best;
        var summary =
            suggestion.AsGood ? Tr.Get("as good")
            : best?.Gain is { } gain ? Words.Gain(gain.Point)
            : best != null ? Tr.Plural("{0} block", "{0} blocks", best.Blocks.Count, best.Blocks.Count)
            : Tr.Get("still stuck");
        var section = new Disclosure(
            "Cs2MasterBlocks",
            Tr.Get("Blocks"),
            summary,
            _blocksOpen,
            open => _blocksOpen = open
        );
        section.Body.AddChild(
            Ui.Label(
                Tr.Format(
                    "Blocks now: {0} · {1} of {2} slots open · worked out from this run, no extra fights",
                    [Named(suggestion.Current), master.Master?.OpenSlots ?? 0, master.Master?.Slots.Count ?? 0]
                ),
                TextStyle.Small,
                wrap: true
            )
        );
        if (best != null)
            section.Body.AddChild(Choice(Tr.Get("Best in your open slots"), best, master, Named));
        if (suggestion.NeedsSlot is { } needs)
            section.Body.AddChild(
                Choice(
                    Tr.Format(
                        "Better · needs slot {0} · {1}",
                        [
                            needs.Blocks.Count,
                            Tr.Plural("{0} more task", "{0} more tasks", needs.TasksNeeded, needs.TasksNeeded),
                        ]
                    ),
                    needs,
                    master,
                    Named
                )
            );
        return section;
    }

    private static PanelContainer Choice(
        string title,
        BlockChoice choice,
        TargetResult master,
        Func<IReadOnlyList<string>, string> named
    )
    {
        var box = Look.Panel(Art.RowBox, "Cs2MasterChoice");
        var column = Ui.Add(box, Ui.Column(2));
        column.AddChild(Ui.Label(title, TextStyle.Gold));
        column.AddChild(Ui.Label(Tr.Format("Block {0}", named(choice.Blocks)), TextStyle.Body, wrap: true));
        if (choice.Gain is { } gain)
        {
            column.AddChild(Ui.Label(Words.Gain(gain.Point), TextStyle.Value));
            column.AddChild(
                Ui.Label(Tr.Format("95% range {0} – {1}", Words.Gain(gain.Low), Words.Gain(gain.High)), TextStyle.Small)
            );
        }
        column.AddChild(Cost(choice, master));
        if (choice.SafeToLeave)
            column.AddChild(Ui.Label(Tr.Get("Safe to leave running: no deaths"), TextStyle.Small));
        // Translators: Button under a block suggestion: put these blocks on side B.
        var tryThese = Ui.Button("", Look.Button4, "Cs2MasterTryBlocks");
        tryThese.Pressed += () => TryBlocks(master, choice.Blocks);
        // Once B blocks exactly these, the button says so instead.
        var onB =
            PanelState.B?.Changes.Find(new ChangeKey(ChangeKind.Blocks, Ids.MasterOf(master.Key)))?.Blocked
                is { } blocked
            && blocked
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    choice.Blocks.Select(k => TargetSpec.FromKey(k).Enemy ?? k).Order(StringComparer.Ordinal)
                );
        tryThese.Disabled = onB;
        SideLine.OnButton(tryThese, onB ? Tr.Format("On {0} ✓", SideLine.B) : Tr.Format("Use on {0}", SideLine.B));
        column.AddChild(tryThese);
        return box;
    }

    // What a choice's blocks cost: the master's coin with the amount, and how long its tasks take to earn it.
    private static Control Cost(BlockChoice choice, TargetResult master)
    {
        if (choice.Cost == 0)
            return Ui.Label(Tr.Get("Costs nothing: these are blocked already"), TextStyle.Small);
        var row = Ui.Row(4, "Cs2MasterBlockCost");
        row.AddChild(Ui.Label(Tr.Get("Costs"), TextStyle.Small));
        if (LiveGame.Find() is { } game && master.Master?.Currency is { Length: > 0 } coin)
        {
            var icon = Ui.Picture(Art.Currency(game, coin), 18);
            icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            icon.TooltipText = LiveGame.NameOf(game, coin);
            row.AddChild(icon);
        }
        row.AddChild(Ui.Label(Ui.Number(choice.Cost), TextStyle.Small));
        if (choice.Hours is { } hours)
            // Translators: How long a Slayer master's tasks take to earn what its blocks cost; {0} is a duration.
            row.AddChild(
                Ui.Label(
                    Tr.Format("· about {0} of {1}'s coins", [Words.Duration(hours * 3600), master.Name]),
                    TextStyle.Small
                )
            );
        return row;
    }

    private static Disclosure Risk(MasterRisk risk, Func<string, string> nameOf)
    {
        var summary =
            risk.Deaths.Point <= 0
                ? Tr.Get("Risk · no deaths")
                : Tr.Format(
                    "Risk · {0} · {1}",
                    [
                        risk.Deaths.Point >= 1
                            ? Tr.Format("1 death in {0} min", Ui.Number(60 / risk.Deaths.Point))
                            : Tr.Format("1 death in {0} h", Ui.Number(1 / risk.Deaths.Point)),
                        risk.MostFrom is { } most ? nameOf(most) : "",
                    ]
                );
        var section = new Disclosure("Cs2MasterRisk", Tr.Get("Risk"), summary, _riskOpen, open => _riskOpen = open);
        section.Body.AddChild(
            Ui.Label(
                Tr.Format(
                    "Deaths {0} {1} · highest hit taken {2} · lowest HP {3}",
                    [
                        Ui.Number(risk.Deaths.Point),
                        Words.PerUnitShort(TimeUnit.Hour),
                        risk.HighestHit is { } hit ? Ui.Number(hit) : "–",
                        risk.LowestHp is { } hp ? Ui.Number(hp) : "–",
                    ]
                ),
                TextStyle.Small,
                wrap: true
            )
        );
        section.Body.AddChild(
            Ui.Label(
                Tr.Get("The game stops at a death: a master with deaths can't be left running."),
                TextStyle.Small,
                wrap: true
            )
        );
        return section;
    }

    // After Find best: the master's best setup in a line, with Use on B and the way to its editor.
    private static PanelContainer? BestLine(TargetResult master)
    {
        if (
            Engine.RunService.Instance.BestByMaster.FirstOrDefault(b => b.Master == master.Key)
            is not { Figure: { } figure } best
        )
            return null;
        var box = Look.Panel(Art.RowBox, "Cs2MasterBest");
        // The words over the buttons: side by side they'd be wider than the details column.
        var column = Ui.Column(4);
        box.AddChild(Ui.Margin(column, 6, 2, 6, 4));
        var words = Ui.Clipped("", TextStyle.Small);
        column.AddChild(words);
        var line = Ui.Add(column, Ui.Row(8));
        // Translators: A master's best setup found by Find best, at the top of its Monsters tab; {0} its figure, {1}
        // its gain over side A.
        SideLine.OnLabel(
            words,
            best.Gain is { } gain
                ? Tr.Format(
                    "Best setup: {0} · {1} vs {2}",
                    [Ui.Number(figure.Point), Words.Gain(gain.Point), SideLine.A]
                )
                : Tr.Format("Best setup: {0}", Ui.Number(figure.Point)),
            TextStyle.Small
        );
        var use = Ui.Button("", Look.Button4, "Cs2MasterBestUse", () => MonsterLoadouts.Use(best));
        use.Disabled = MonsterLoadouts.IsOnB(best);
        SideLine.OnButton(use, use.Disabled ? Tr.Format("On {0} ✓", SideLine.B) : Tr.Format("Use on {0}", SideLine.B));
        line.AddChild(use);
        // Translators: Link from a master's Monsters tab to its editor on the Setup tab's Slayer page.
        line.AddChild(
            Ui.Button(
                Tr.Get("On Setup ›"),
                Look.Button4,
                "Cs2MasterBestSetup",
                () =>
                {
                    SetupTab.SlayerMasters.OpenMaster(master.Key);
                    SetupTab.SetupTabView.SetUp(SideLetter.B, SetupPage.Slayer);
                }
            )
        );
        return box;
    }

    // Puts the blocks on side B beside side A's changes, made from side A when there is no side B.
    private static void TryBlocks(TargetResult master, IReadOnlyList<string> blocks)
    {
        var enemies = blocks.Select(k => TargetSpec.FromKey(k).Enemy ?? k).ToList();
        var change = new Change(
            ChangeKind.Blocks,
            Ids.MasterOf(master.Key),
            enemies.Count == 0 ? Change.None : string.Join(',', enemies)
        );
        var a = PanelState.A;
        var b = PanelState.B is { CanChange: true } existing
            ? existing
            : new SideState(a.Base, a.Changes, a.IgnoreRequirements);
        PanelState.SetSide(SideLetter.B, b.WithChanges(b.Changes.With(change)));
    }
}
