namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// One thing every simulated fight assumes, listed in each result's assumptions. Four of them are also shown to the
/// player as "In every fight" when the panel explains how CS2 works, so the two lists can't drift apart.
/// </summary>
public sealed record Assumption(string Key, Note Note, bool InEveryFight)
{
    public const string FullHp = "full-hp";
    public const string NoItemLoss = "no-item-loss";
    public const string Consumables = "consumables";
    public const string Levels = "levels";

    /// <summary>Every fight's assumptions, in the order results list them.</summary>
    public static readonly IReadOnlyList<Assumption> All =
    [
        new(FullHp, Note.Of("clean start (combat stopped, HP full)"), InEveryFight: true),
        new(NoItemLoss, Note.Of("no item loss on death"), InEveryFight: true),
        new("refight", Note.Of("fights again after a death"), InEveryFight: false),
        new(Consumables, Note.Of("unlimited consumables (topped up after each tick)"), InEveryFight: true),
        new(Levels, Note.Of("levels held (XP reset after each trial)"), InEveryFight: true),
        new("loot-cleared", Note.Of("loot cleared from the bank after it is counted"), InEveryFight: false),
        // The copy keeps the save's slayer task for the whole run; only auto-slayer is off, so the copy never switches
        // enemy.
        new(
            SlayerOff,
            Note.Of("slayer task as in the save, for the whole run (it never runs out), auto-slayer off"),
            InEveryFight: false
        ),
        new("noon", Note.Of("time of day frozen at 12:00"), InEveryFight: false),
    ];

    /// <summary>The key of the assumption a master's result leaves out: its monsters are fought on its task.</summary>
    public const string SlayerOff = "slayer-off";

    /// <summary>
    /// What a master's figures assume about playing its tasks, listed once on a master's result in place of the
    /// save's task and never on a monster's own.
    /// </summary>
    public static readonly IReadOnlyList<Assumption> MasterAll =
    [
        new("auto-slayer", Note.Of("Auto Slayer on, starting in the task's area"), InEveryFight: false),
        new("whole-tasks", Note.Of("every task fought to the end; no rerolls or extensions"), InEveryFight: false),
        new("slayer-level", Note.Of("Slayer level as in the setup"), InEveryFight: false),
        new("one-respawn", Note.Of("one respawn between tasks"), InEveryFight: false),
        new(
            "restart-after-death",
            Note.Of("you start again after each death: the game stops, so a master with deaths can't be left running"),
            InEveryFight: false
        ),
        new("locked-areas", Note.Of("Auto Slayer enters areas you haven't unlocked"), InEveryFight: false),
        new("hour-a-kill", Note.Of("each monster given up to an hour a kill"), InEveryFight: false),
    ];

    /// <summary>Listed on a master's result when any of its monsters is fought with a loadout of its own.</summary>
    public static readonly Assumption FreeSwaps = new(
        "free-swaps",
        Note.Of("Loadout swaps between tasks cost no time (swapped while the next monster spawns)"),
        InEveryFight: false
    );

    /// <summary>A master's assumptions: every fight's, without the save's task, then the master's own.</summary>
    public static IReadOnlyList<Assumption> ForMaster => [.. All.Where(a => a.Key != SlayerOff), .. MasterAll];

    /// <summary>
    /// Listed for a setup that adds or takes away a pet: the copy gets or loses the pet's bonuses, as the game gives
    /// them for a found pet, but no call marks a pet found, so the found count stays the save's.
    /// </summary>
    public static readonly Assumption PetsChanged = new(
        "pets-changed",
        Note.Of(
            "Pets added or taken away here change their bonuses only; the game still counts the pets you've found."
        ),
        InEveryFight: false
    );

    /// <summary>
    /// The assumption listed only for a setup with a change that lowers a level: the game never checks again what is
    /// already worn or prayed, so it stays.
    /// </summary>
    public static readonly Assumption LevelsLowered = new(
        "levels-lowered",
        Note.Of("items and prayers already in use stay when a level is lowered"),
        InEveryFight: false
    );
}
