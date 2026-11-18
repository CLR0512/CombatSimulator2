using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Slayer;

/// <summary>
/// A master's monsters on saved loadouts of their own, on its Monsters tab: a gold tag beside a monster fought with
/// one, a picker per monster only while editing, and Best loadouts with its progress, cancel and picks. Every choice is
/// one loadouts change on side B, beside side A's changes, as blocks are tried; nothing changes the game.
/// </summary>
internal static class MonsterLoadouts
{
    /// <summary>
    /// The loadout a monster fought with in this run, as a tag beside its name; null for the setup's.
    /// </summary>
    public static Label? Tag(TargetResult master, string target, Func<string, string> names)
    {
        if (master.Loadouts?.GetValueOrDefault(target) is not { } slot)
            return null;
        var tag = Ui.Label(names(slot), TextStyle.Tiny, "Cs2MasterLoadoutTag");
        Tint(tag, Usable().ContainsKey(slot) ? Look.Best : Look.Deadly);
        return tag;
    }

    /// <summary>What Best loadouts found for a monster, beside its tag: its gain, or the choices it ties with.</summary>
    public static Label? Pick(TargetResult master, string target, Func<string, string> names)
    {
        if (
            RunService.Instance.BestLoadoutsFor(master.Key)
                is not { State: BestLoadoutsState.Done, Picks: { } picks } job
            || job.Master != master.Key
            || picks.Picks.FirstOrDefault(p => Ids.PartTarget(p.Monster) == target) is not { } pick
        )
            return null;
        // Translators: Beside a monster that Best loadouts found ties for; the choices as good as its pick are its tooltip.
        var text =
            pick.Gain is { } gain ? Words.Gain(gain.Point)
            : pick.Ties.Count > 0 ? Tr.Get("tie")
            : null;
        if (text == null)
            return null;
        var label = Ui.Label(text, TextStyle.Tiny, "Cs2MasterLoadoutPick");
        if (pick.Ties.Count > 0)
        {
            label.TooltipText = Tr.Format("As good: {0}", string.Join(", ", pick.Ties.Select(t => Choice(t, names))));
            label.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        if (pick.Gain != null)
            Tint(label, pick.Gain.Point >= 0 ? Look.Safe : Look.Deadly);
        return label;
    }

    /// <summary>
    /// After Find best loadouts on this master: which monsters do better on another loadout, grouped by loadout, and the
    /// master's gain with them on side B. Null before it, and when every monster keeps the setup.
    /// </summary>
    public static PanelContainer? Banner(TargetResult master, Func<string, string> names)
    {
        if (
            RunService.Instance.BestLoadoutsFor(master.Key)
                is not { State: BestLoadoutsState.Done, Picks: { } picks } job
            || job.Master != master.Key
            || picks.Picks.Where(p => p.Choice != null).ToList() is not { Count: > 0 } better
        )
            return null;
        string Monster(LoadoutPick p) => names(TargetSpec.FromKey(Ids.PartTarget(p.Monster)).Enemy ?? p.Monster);
        var lines = better
            .GroupBy(p => p.Choice!)
            .Select(g => Tr.Format("{0}: {1}", [names(g.Key), string.Join(", ", g.Select(Monster))]))
            .ToList();
        if (picks.Gain is { } gain)
            // Translators: The last line of Find best loadouts' banner: the master's gain with the picks.
            lines.Add(
                Tr.Format(
                    "{0} for the master with them. Use on B on the Slayer setup page to try them.",
                    Words.Gain(gain.Point)
                )
            );
        // Translators: Banner title after Find best loadouts on a Slayer master.
        return Widgets.Banner.Make(
            "Cs2BestLoadoutsBanner",
            Look.Safe,
            null,
            Tr.Plural(
                "{0} monster does better on another loadout",
                "{0} monsters do better on another loadout",
                better.Count,
                better.Count
            ),
            string.Join("\n", lines)
        );
    }

    /// <summary>
    /// The Loadouts section: what the tags mean, and the link to the Setup tab's Slayer page, where the side's loadouts
    /// and blocks for this master are changed and its best is found.
    /// </summary>
    public static VBoxContainer Section(TargetResult master)
    {
        var section = Ui.Column(4);
        section.Name = "Cs2MasterLoadouts";
        var head = Ui.Add(section, Ui.Row(8));
        var title = Ui.Label(Tr.Get("Loadouts"), TextStyle.Heading);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        // Translators: Link on a master's Monsters tab to its editor on the Setup tab's Slayer page.
        var edit = Ui.Button(
            Tr.Get("Edit on Setup ›"),
            Look.Button4,
            "Cs2MasterLoadoutsEdit",
            () => EditOnSetup(master)
        );
        edit.TooltipText = Tr.Get("Change this master's blocks and loadouts per monster, and find its best");
        head.AddChild(edit);
        section.AddChild(
            Ui.Label(
                Tr.Get("Monsters fight with the setup unless given a saved loadout; a tag names it."),
                TextStyle.Small,
                wrap: true
            )
        );
        return section;
    }

    // The Setup tab's Slayer page on side B (made from A when there is none), with this master's editor open.
    private static void EditOnSetup(TargetResult master)
    {
        SetupTab.SlayerMasters.OpenMaster(master.Key);
        SetupTab.SetupTabView.SetUp(SideLetter.B, SetupPage.Slayer);
    }

    /// <summary>
    /// Best loadouts' picks on side B, in place of that master's loadouts there: side B is made from side A when there
    /// is none, and other masters' picks on it stay, so several masters' picks compare at once.
    /// </summary>
    public static void PutPicks(BestLoadouts job)
    {
        if (job.Picks is not { } picks)
            return;
        var a = PanelState.A;
        var b = PanelState.B is { CanChange: true } existing
            ? existing
            : new SideState(a.Base, a.Changes, a.IgnoreRequirements);
        var changes = b.Changes.Without(new ChangeKey(ChangeKind.Loadouts, Ids.MasterOf(job.Master)));
        if (picks.Change is { } change)
            changes = changes.With(change);
        PanelState.SetSide(SideLetter.B, b.WithChanges(changes));
    }

    /// <summary>
    /// One master's best loadouts and blocks on side B (made from side A when there is none), in place of that master's
    /// loadouts and blocks there; every other change on B stays.
    /// </summary>
    public static void Use(BestMaster best)
    {
        ArgumentNullException.ThrowIfNull(best);
        Use([best]);
    }

    /// <summary>Every master's best of <paramref name="run"/> on side B, as <see cref="Use(BestMaster)"/> puts one.</summary>
    public static void UseAll(BestMastersRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Use(run.Done);
    }

    private static void Use(IEnumerable<BestMaster> masters)
    {
        var a = PanelState.A;
        var b = PanelState.B is { CanChange: true } existing
            ? existing
            : new SideState(a.Base, a.Changes, a.IgnoreRequirements);
        var changes = b.Changes;
        foreach (var master in masters.Where(m => m.Figure != null))
        {
            var id = Ids.MasterOf(master.Master);
            changes = changes
                .Without(new ChangeKey(ChangeKind.Loadouts, id))
                .Without(new ChangeKey(ChangeKind.Blocks, id));
            if (master.Loadouts is { } loadouts)
                changes = changes.With(loadouts);
            if (master.Blocks is { } blocks)
                changes = changes.With(blocks);
        }
        PanelState.SetSide(SideLetter.B, b.WithChanges(changes));
    }

    /// <summary>Whether side B gives the master exactly its best loadouts and blocks.</summary>
    public static bool IsOnB(BestMaster best)
    {
        ArgumentNullException.ThrowIfNull(best);
        if (PanelState.B is not { } b)
            return false;
        var id = Ids.MasterOf(best.Master);
        return b.Changes.Find(new ChangeKey(ChangeKind.Loadouts, id))?.Value == best.Loadouts?.Value
            && b.Changes.Find(new ChangeKey(ChangeKind.Blocks, id))?.Value == best.Blocks?.Value;
    }

    // The character's unlocked loadouts, slot to name.
    private static Dictionary<string, string> Usable() =>
        LiveGame.Find() is { } game
            ? LiveGame
                .Loadouts(game)
                .Where(l => l.Unlocked)
                .ToDictionary(l => l.Slot, l => l.Name, StringComparer.Ordinal)
            : [];

    // A label in its style's size and weight, in another colour: a label takes its colour from its settings.
    private static void Tint(Label label, Color colour)
    {
        var settings = (LabelSettings)label.LabelSettings.Duplicate();
        settings.FontColor = colour;
        label.LabelSettings = settings;
    }

    private static string Choice(string? slot, Func<string, string> names) =>
        slot == null ? Tr.Get("Same as setup") : names(slot);
}
