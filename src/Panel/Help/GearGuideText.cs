using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// What the Gear tab's guide says, in the game's language. Lines that state a number follow the player's settings
/// and the search's own limits, so they stay true when either changes. Main thread.
/// </summary>
internal static class GearGuideText
{
    // Translators: Title of the window that explains the Gear tab's two searches.
    public static string Title => Tr.Get("Gear search");

    public static string Lead =>
        Tr.Get("Each set fights the target as in a run. The best by your Rank by stat wins, within your Deaths limit.");

    public static string TopGear => Tr.Get("Find best setup");

    public static string TopGearWhat => Tr.Get("Best mix of what you own: gear, plus what's on under Also try.");

    public static string GatherTitle => Tr.Get("Gather");

    public static string Gather => Tr.Get("Everything you own and can wear now.");

    public static string GatherDetail =>
        Lines(
            Tr.Get("Bank, worn and your other loadouts. Locked slots stay; items marked Never use are left out."),
            Tr.Get(
                "With Include what I can't use yet, choices you lack and gear you can't wear yet are tried too, and marked."
            )
        );

    public static string RuleOutTitle => Tr.Get("Rule out");

    public static string RuleOut => Tr.Get("Sets beaten in every stat skip the fight.");

    public static string RuleOutDetail =>
        Lines(
            Tr.Get("Beaten or matched in every stat that counts against this target."),
            Tr.Get("Sets with the same stats against the target fight once.")
        );

    public static string RaceTitle => Tr.Get("Race");

    /// <summary>The stages' trial counts as the settings give them: "100 → 400 → 1,000".</summary>
    public static string Race(int trials) =>
        Tr.Format(
            "Fights in stages: {0} trials.",
            string.Join(" → ", Answers.Stages(trials).Select(t => Ui.Number(t)))
        );

    public static string RaceDetail =>
        Lines(
            Tr.Get("A set clearly behind the leader, or over the death limit, drops out after each stage."),
            Tr.Get("Your current gear always stays in, so the gain can be measured."),
            Tr.Format(
                "{0} trials first when there are {1} sets or more.",
                [Ui.Number(Answers.FirstRung), Ui.Number(Answers.ManySets)]
            )
        );

    public static string ClimbTitle => Tr.Get("Too many sets");

    public static string Climb =>
        Tr.Format("Over {0}: improves one slot at a time.", Ui.Number(Planner.MostCandidates));

    public static string ClimbDetail =>
        Lines(
            Tr.Format(
                "Also when fighting them all would take over {0} min, or before any search is timed, over {1} sets.",
                [Ui.Number(PlanPreview.LongSearchSeconds / 60), Ui.Number(PlanPreview.MostUntimed)]
            ),
            Tr.Format(
                "Weapon first, then each slot, then set pieces together, until nothing changes or {0} min pass.",
                Ui.Number(PlanPreview.LongSearchSeconds / 60)
            ),
            Tr.Get("A close second weapon gets its own climb. A better combination may exist.")
        );

    // Translators: Gear guide step: trying prayers, spell, imbuement, food and toolbelt after the gear.
    public static string BeyondTitle => Tr.Get("Beyond gear");

    public static string Beyond => Tr.Get("Then prayers, spell, food and the rest, in turn.");

    public static string BeyondDetail =>
        Lines(
            Tr.Format("Prayers one at a time, then pairs of the best {0}.", Ui.Number(Choices.PairsFrom)),
            Tr.Get("When a choice changes, the gear is searched again around it, up to three passes."),
            Tr.Get("Two too close to call fight again, four times as long; the answer names the runner-up.")
        );

    public static string AnswerTitle => Tr.Get("Answer");

    public static string Answer => Tr.Get("Best set and its gain, or keep yours if as good.");

    public static string AnswerDetail =>
        Lines(
            Tr.Get("The gain over your setup, with a 95% range, and why it wins."),
            Tr.Get(
                "Gear alone: of the sets within the best's range or the margin, the one taking least damage; yours if it takes no more."
            ),
            Tr.Get("With Also try: yours if within the best's range, else the highest gain."),
            Tr.Get("Cheaper and Safer: as good, using less or taking less damage. Open as setup puts one on side B.")
        );

    public static string Droptimizer => Tr.Get("Find upgrades");

    public static string DroptimizerWhat => Tr.Get("Items you don't own yet, by what they'd gain.");

    public static string DropGather => Tr.Get("Every item you don't own and can wear.");

    public static string DropGatherDetail =>
        Tr.Get("Items you can't wear yet are listed with what they need, and never fought.");

    public static string DropRuleOut => Tr.Get("Items beaten in every stat skip the fight.");

    public static string DropRuleOutDetail =>
        Tr.Get("Beaten or matched by another unowned item for its slot, in every stat that counts.");

    public static string SwapTitle => Tr.Get("Swap and fight");

    public static string Swap => Tr.Get("Each item alone in your gear, raced against it.");

    public static string SwapDetail => Tr.Get("The same stages as Find best setup, judged against your gear alone.");

    public static string DropAnswer => Tr.Get("Gains by source, largest first. Click one to try it on B.");

    public static string DropAnswerDetail =>
        Lines(
            Tr.Get("Safer lists items as good that take clearly less damage."),
            Tr.Get("Click others to try them together; click one again to take it out.")
        );

    // Translators: Gear search guide button that hides the Gear tab's (i) button; Settings shows it again.
    public static string HideButton => Tr.Get("Hide guide button");

    public static string HideButtonHover => Tr.Get("Hides the (i) on the Gear tab. Settings shows it again.");

    public static string Kept => Tr.Get("Answers are kept per character and target.");

    private static string Lines(params string[] lines) => string.Join("\n", lines);
}
