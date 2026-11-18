using System.Globalization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// Changes in plain words, for a changed setup's results and its label. Names come from a resolver (the game's
/// display names), so the model stays free of the game.
/// </summary>
public static class ChangeNotes
{
    /// <summary>
    /// One note per change that applies or was left out ("Strength 50 → 60", "left out: … (why)", "…: requirement
    /// ignored (…)"), none for a change the setup already had, then the levels-lowered assumption when a level goes
    /// down and the pets-changed one when a pet is added or taken away.
    /// </summary>
    public static List<Note> Of(IEnumerable<ChangeOutcome> outcomes, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(name);
        var notes = new List<Note>();
        var lowered = false;
        var pets = false;
        foreach (var outcome in outcomes)
        {
            var what = Describe(outcome.Change, outcome.From, name);
            switch (outcome.State)
            {
                case ChangeState.Accepted:
                    notes.Add(what);
                    break;
                case ChangeState.RequirementIgnored:
                    notes.Add(Note.Of("{0}: requirement ignored ({1})", what, Joined(outcome.Reasons)));
                    break;
                case ChangeState.Refused:
                    notes.Add(Note.Of("left out: {0} ({1})", what, Joined(outcome.Reasons)));
                    break;
                case ChangeState.NotInBuild:
                    notes.Add(Note.Of("left out: {0} (not in this build)", what));
                    break;
                default:
                    break;
            }
            pets |= outcome.Applies && outcome.Change.Kind == ChangeKind.Pet;
            lowered |=
                outcome.Applies
                && outcome.Change.Level is { } level
                && int.TryParse(outcome.From, NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                && level < from;
        }
        if (lowered)
            notes.Add(Assumption.LevelsLowered.Note);
        if (pets)
            notes.Add(Assumption.PetsChanged.Note);
        return notes;
    }

    /// <summary>
    /// What a result didn't simulate: the side effects its fights' guards blocked, then each part of this setup and
    /// target CS2 doesn't model, by its name.
    /// </summary>
    public static IReadOnlyList<Note> NotSimulated(TargetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return [.. result.Unsupported, .. (result.NotSimulated ?? []).Select(g => Note.Of("{0}: {1}", g.Name, g.What))];
    }

    /// <summary>A result's assumptions, then its run's change notes when the run's setup has changes.</summary>
    public static IReadOnlyList<Note> Assumptions(Run run, TargetResult result, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(result);
        return run.ChangeOutcomes is not { Count: > 0 } outcomes
            ? result.Assumptions
            : [.. result.Assumptions, .. Of(outcomes, name)];
    }

    /// <summary>A changed setup's name, such as "current + 2 changes".</summary>
    public static Note Label(string setupName, int count) =>
        Note.Plural("{0} + {1} change", "{0} + {1} changes", count, setupName, count);

    /// <summary>One change from <paramref name="from"/> (null: unknown) in plain words.</summary>
    public static Note Describe(Change change, string? from, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(name);
        var before = from ?? "?";
        return change.Kind switch
        {
            ChangeKind.Level => Note.Of("{0} {1} → {2}", name(change.Subject!), before, change.Value),
            ChangeKind.Stance => Note.Of("Stance: {0} → {1}", name(before), name(change.Value)),
            ChangeKind.Spell => before == Change.None
                ? Note.Of("Spell: none → {0}", name(change.Value))
                : Note.Of("Spell: {0} → {1}", name(before), name(change.Value)),
            ChangeKind.Imbuement => (before == Change.None, change.Value == Change.None) switch
            {
                (true, _) => Note.Of("Imbuement: none → {0}", name(change.Value)),
                (_, true) => Note.Of("Imbuement: {0} → none", name(before)),
                _ => Note.Of("Imbuement: {0} → {1}", name(before), name(change.Value)),
            },
            ChangeKind.Prayer => change.TurnsOn
                ? Note.Of("{0} on", name(change.Subject!))
                : Note.Of("{0} off", name(change.Subject!)),
            ChangeKind.Quest => change.Completes
                ? Note.Of("Quest {0} done", name(change.Subject!))
                : Note.Of("Quest {0} not done", name(change.Subject!)),
            ChangeKind.Shop => Note.Of("{0}: {1} → {2} bought", name(change.Subject!), before, change.Value),
            ChangeKind.Guild => Note.Of("{0} guild rank {1} → {2}", name(change.Subject!), before, change.Value),
            ChangeKind.Star => Note.Of("Star {0} on", name(change.Subject!)),
            ChangeKind.Equip or ChangeKind.Toolbelt => (before == Change.None, change.Item == null) switch
            {
                (true, _) => Note.Of("{0}: empty → {1}", name(change.Subject!), name(change.Value)),
                (_, true) => Note.Of("{0}: {1} → empty", name(change.Subject!), name(before)),
                _ => Note.Of("{0}: {1} → {2}", name(change.Subject!), name(before), name(change.Value)),
            },
            ChangeKind.Blocks => Note.Of(
                "{0} blocks: {1} → {2}",
                name(change.Subject!),
                Enemies(before, name),
                Enemies(change.Value, name)
            ),
            ChangeKind.Loadouts => Note.Of(
                "{0} loadouts: {1} → {2}",
                name(change.Subject!),
                MonsterLoadouts(before, name),
                MonsterLoadouts(change.Value, name)
            ),
            ChangeKind.SlayerTask => change.Value == Change.On
                ? Note.Of("Single monsters on a slayer task")
                : Note.Of("Single monsters off any slayer task"),
            ChangeKind.SlayerTaskFor => change.Value switch
            {
                Change.On => Note.Of("{0}: on a slayer task", TargetName(change.Subject!, name)),
                Change.Off => Note.Of("{0}: off any slayer task", TargetName(change.Subject!, name)),
                _ => Note.Of("{0}: slayer task as the save", TargetName(change.Subject!, name)),
            },
            ChangeKind.Food => (before == Change.None, change.Item == null) switch
            {
                (true, _) => Note.Of("Food: none → {0}", name(change.Value)),
                (_, true) => Note.Of("Food: {0} → none", name(before)),
                _ => Note.Of("Food: {0} → {1}", name(before), name(change.Value)),
            },
            ChangeKind.Pet => change.Value == Change.On
                ? Note.Of("{0} found", name(change.Subject!))
                : Note.Of("{0} removed", name(change.Subject!)),
            ChangeKind.Bonfire => change.Tier is { } tier
                ? Note.Of("{0} bonfire, burning", name(tier))
                : Note.Of("Bonfire out"),
            ChangeKind.Technique => Note.Of("{0} {1} → {2}", name(change.Subject!), before, change.Value),
            _ => (before == Change.None, change.Item == null) switch
            {
                (true, _) => Note.Of("Potion: none → {0}", name(change.Value)),
                (_, true) => Note.Of("Potion: {0} → none", name(before)),
                _ => Note.Of("Potion: {0} → {1}", name(before), name(change.Value)),
            },
        };
    }

    // A blocks value as names: "Jack O'Lantern, Scarecrow", or none.
    private static string Enemies(string value, Func<string, string> name) =>
        value is Change.None or "?" ? value : string.Join(", ", value.Split(',').Select(name));

    // A loadouts value as names: "Golbin: Loadout 2, Scarecrow: Loadout 1" for two monsters at most; past that, how
    // many are on each loadout, "Loadout 1 ×7, Loadout 3 ×1", so a whole pool's change stays one short line.
    private static string MonsterLoadouts(string value, Func<string, string> name)
    {
        if (value is Change.None or "?")
            return value;
        var pairs = value.Split(',').Select(p => p.Split('@')).Where(p => p.Length == 2).ToList();
        if (pairs.Count == 0)
            return value;
        return pairs.Count <= 2
            ? string.Join(", ", pairs.Select(p => name(p[0]) + ": " + name(p[1])))
            : string.Join(", ", pairs.GroupBy(p => p[1]).Select(g => name(g.Key) + " ×" + g.Count()));
    }

    // A monster's target key by the enemy's name.
    private static string TargetName(string key, Func<string, string> name) => name(key[(key.LastIndexOf('/') + 1)..]);

    // Several reasons as one note, joined with "; ".
    private static Note Joined(List<Note> reasons) =>
        reasons.Count == 0 ? Note.Of("no reason given")
        : reasons.Count == 1 ? reasons[0]
        : reasons.Skip(1).Aggregate(reasons[0], (all, next) => Note.Of("{0}; {1}", all, next));
}
