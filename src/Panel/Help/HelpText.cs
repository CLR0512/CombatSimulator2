using System.Globalization;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// What the Guide window and the tour say, in the game's language. Lines that state a number or how runs
/// work follow the player's settings, so they stay true when the settings change. Main thread.
/// </summary>
internal static class HelpText
{
    public static string Title => Tr.Get("Guide", "CS2 help");

    public static string Lead =>
        Tr.Get("CS2 runs the game's combat code on a copy of your character and records the results.");

    public static string SnapshotTitle => Tr.Get("Snapshot");

    public static string Snapshot =>
        Tr.Get("CS2 saves your character to memory, including gear, prayers, potions and bonuses.");

    public static string FightTitle => Tr.Get("Fight");

    /// <summary>Where copies fight, how many times, and "up to" when targets may stop early.</summary>
    public static string Fight(Preferences prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var count = prefs.Trials;
        var shown = count.ToString("#,0", Tr.Culture);
        var inGame = prefs.SimulateIn == Preferences.InGame;
        return (inGame, prefs.StopEarly) switch
        {
            (false, false) => Tr.Plural(
                "A separate copy of the game fights each target {0} time.",
                "A separate copy of the game fights each target {0} times.",
                count,
                shown
            ),
            (false, true) => Tr.Plural(
                "A separate copy of the game fights each target up to {0} time.",
                "A separate copy of the game fights each target up to {0} times.",
                count,
                shown
            ),
            (true, false) => Tr.Plural(
                "Copies inside your game fight each target {0} time.",
                "Copies inside your game fight each target {0} times.",
                count,
                shown
            ),
            (true, true) => Tr.Plural(
                "Copies inside your game fight each target up to {0} time.",
                "Copies inside your game fight each target up to {0} times.",
                count,
                shown
            ),
        };
    }

    public static string MeasureTitle => Tr.Get("Measure");

    public static string Measure(TimeUnit unit) =>
        Tr.Format("CS2 turns the results into rates {0}, each with a range.", Words.PerUnit(unit));

    /// <summary>The example result's label: "XP per hour".</summary>
    public static string SampleLabel(TimeUnit unit) => Tr.Format("XP {0}", Words.PerUnit(unit));

    public static string SampleRange => Tr.Get("CS2 is 95% sure the rate is in this range.");

    // An example XP rate and its range per hour, scaled to the unit shown (per kill: a 2.1 s kill).
    private const double SampleHourly = 4410;
    private const double SampleLowHourly = 4350;
    private const double SampleHighHourly = 4470;
    private const double SampleKillSeconds = 2.1;

    public static (double Value, double Low, double High) Sample(TimeUnit unit)
    {
        var factor = (unit == TimeUnit.Kill ? SampleKillSeconds : TimeUnits.Seconds(unit)) / 3600;
        return (SampleHourly * factor, SampleLowHourly * factor, SampleHighHourly * factor);
    }

    public static string EveryFight => Tr.Get("In every fight");

    public static string AllListed => Tr.Get("Each result lists all of its assumptions.");

    // Translators: Heading over the Guide's rows that say what CS2 can do: simulate, compare, find gear, Slayer.
    public static string WhatYouCanDo => Tr.Get("What you can do");

    public static string SimulateTitle => Tr.Get("Simulate");

    public static string SimulateLine => Tr.Get("Rank every target by any rate. Click one for its fights.");

    /// <summary>What a result's fight details, close calls and ran over mean.</summary>
    public static string SimulateDetail =>
        string.Join(
            "\n",
            Tr.Get("Fight details: chance to hit and damage, both ways, with ranges."),
            Tr.Get("Close calls: fights won where one more top hit would have killed."),
            Tr.Get("Ran over: a fight past the time limit counts as no kill and no death.")
        );

    public static string CompareTitle => Tr.Get("Compare");

    public static string CompareLine => Tr.Get("Setup A against B, target by target.");

    /// <summary>Where side B comes from, what each target shows, and the compare view's controls.</summary>
    public static string CompareDetail =>
        string.Join(
            "\n",
            Tr.Get("+ Compare starts B from a copy of yours, a loadout, a preset or a saved run."),
            Tr.Get("Each target shows which side is better and by how much."),
            Tr.Get("Better and Worse show only where B is clearly ahead or behind."),
            Tr.Get("⇅ swaps the sides; ✕ Clear goes back to one setup.")
        );

    // Translators: Guide row title for the Gear tab's two searches.
    public static string GearTitle => Tr.Get("Find gear");

    public static string GearLine => Tr.Get("Best set of what you own; upgrades you don't.");

    /// <summary>When a search counts two sets as equal, at the margin in Settings, and how it scores many targets.</summary>
    public static string GearDetail =>
        string.Join(
            "\n",
            Tr.Format(
                "A set within the best's 95% range or the margin ({0}) is as good; then yours wins.",
                Ui.Percent(PanelState.Prefs.TieShare)
            ),
            Tr.Get("For an area or the pins, each target counts the same."),
            Tr.Get("How it searches: (i) on the Gear tab.")
        );

    // Translators: Guide row title for Slayer masters and the Slayer page.
    public static string SlayerTitle => Tr.Get("Slayer");

    public static string SlayerLine => Tr.Get("Masters as targets; best loadouts and blocks per master.");

    /// <summary>How a master is fought, what the Slayer page tries, and Your tasks.</summary>
    public static string SlayerDetail =>
        string.Join(
            "\n",
            Tr.Get("A master's monsters fight on task, weighted by how long their tasks take."),
            Tr.Get("The Slayer page tries your loadouts and blocks; nothing changes in your game."),
            Tr.Get("Your tasks: which to extend or reroll.")
        );

    public static string Untouched => Tr.Get("CS2 does not change or edit your game.");

    public static string StartTour => Tr.Get("Start tour");

    public static string Close => Tr.Get("Close");

    /// <summary>The "In every fight" lines, in the order they are shown, by assumption key, with their icons.</summary>
    public static readonly IReadOnlyList<string> LineOrder =
    [
        Assumption.FullHp,
        Assumption.Consumables,
        Assumption.Levels,
        Assumption.NoItemLoss,
    ];

    /// <summary>
    /// An assumption's "In every fight" line and icon path, or null for one the explainer has no line for.
    /// </summary>
    public static (string Text, string Icon)? Line(string key) =>
        key switch
        {
            Assumption.FullHp => (Tr.Get("You start at full HP"), Art.HitpointsPath),
            Assumption.Consumables => (Tr.Get("Food and ammo never run out"), Art.InfinityPath),
            Assumption.Levels => (Tr.Get("Your levels stay the same"), Art.XpPath),
            Assumption.NoItemLoss => (Tr.Get("You lose no items on death"), Art.BankPath),
            _ => null,
        };

    public static string SkipTour => Tr.Get("Skip tour");

    public static string Back => Tr.Get("Back");

    public static string Next => Tr.Get("Next");

    public static string Done => Tr.Get("Done");

    public static string Count(int step, int total) =>
        Tr.Format(
            "{0} of {1}",
            [step.ToString(CultureInfo.InvariantCulture), total.ToString(CultureInfo.InvariantCulture)]
        );

    public static (string Title, string Text) Step(int step, bool alternate, bool chart) =>
        step switch
        {
            1 => (
                Tr.Get("Your setup"),
                Tr.Get(
                    "What you fight with. Click a slot to change it; right-click puts it back or empties it. Edit opens the Setup tab for levels, prayers, spells and more."
                )
            ),
            2 => (
                Tr.Get("Targets"),
                Tr.Get(
                    "Open this to see what will be simulated. Click a monster to ignore it. The filters to its left narrow the list."
                )
            ),
            3 => (
                alternate ? Tr.Get("Two setups") : Tr.Get("Compare"),
                alternate
                    ? Tr.Get("A is compared to B, target by target. ⇅ swaps them. ✕ Clear goes back to one setup.")
                    : Tr.Get(
                        "+ Compare adds a second setup: a copy of yours to change, a loadout, a preset or a saved run. Each target then shows which is better and by how much."
                    )
            ),
            4 => (
                Tr.Get("Simulate"),
                alternate
                    ? Tr.Get("A run is going. This button cancels it.")
                    // How it fights, from the settings the run will use.
                    : Fight(PanelState.Prefs) + " " + Tr.Get("Each result shows as its target finishes.")
            ),
            _ => (
                Tr.Get("Read the results"),
                alternate ? Tr.Get("Results show here after a run. Rank them by any stat. Click one for its details.")
                : chart
                    ? Tr.Get(
                        "Each bar is a target: its height is the stat you rank by, its colour how often you die there, its whisker the 95% range. Hover for numbers, click for details, shift-click to pin. Click a portrait to ignore that target, or an area's name to fold it into one bar."
                    )
                : Tr.Get("Rank by any stat. Click a row for its details. Pin up to three, then View side by side.")
            ),
        };
}
