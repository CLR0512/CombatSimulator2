namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Puts a prepared copy back as it was after changes were tried in it: the gear through the game's own equip
/// calls, then the prayers, spell, imbuement, food and toolbelt through theirs, the bank's counts through the bank's,
/// the bonfire's bonus, then a reset for the rest. A reset alone isn't enough, because the
/// character's stats follow events and reading a component back from its saved bytes raises none; so the stats the
/// game shows are compared with the prepared ones at the end, whatever was changed.
/// </summary>
internal sealed class CopyUndo
{
    private readonly Copy _copy;
    private readonly GearUndo _gear;
    private readonly ChoiceUndo _choices;
    private readonly BankCounts _bank;
    private readonly BonfireChange.Kept? _bonfire;
    private readonly List<string> _stats;

    private CopyUndo(Copy copy)
    {
        _copy = copy;
        _gear = GearUndo.Of(copy);
        _choices = ChoiceUndo.Of(copy);
        _bank = BankCounts.Of(copy);
        _bonfire = Dependencies.IsAvailable(Dependencies.Firemaking) ? BonfireChange.Keep(copy) : null;
        _stats = Stats(copy);
    }

    /// <summary>Records <paramref name="copy"/> as it is now; call it right after <see cref="Copy.KeepPristine"/>.</summary>
    public static CopyUndo Of(Copy copy) => new(copy);

    /// <summary>
    /// Undoes what was changed since. Returns what still differs from the prepared copy, so an empty list means it
    /// may be used again; otherwise dispose it.
    /// </summary>
    public IReadOnlyList<string> Undo()
    {
        var refused = _gear.Restore();
        if (refused.Count > 0)
            return [.. refused.Select(outcome => $"gear not put back: {outcome.Change.ShortForm}")];
        // After the gear: a spell needs the weapon's elements back first.
        refused = _choices.Restore();
        if (refused.Count > 0)
            return [.. refused.Select(outcome => $"choice not put back: {outcome.Change.ShortForm}")];
        _bank.Restore();
        if (_bonfire != null)
            BonfireChange.Restore(_copy, _bonfire);
        var differing = _copy.Reset();
        if (differing.Count > 0)
            return differing;
        // Gear taken off can leave a running total a hair from zero, which the stats below don't show.
        using (Guards.Enter())
            ModifierTotals.Settle(_copy.Game);
        var now = Stats(_copy);
        return [.. now.Where((line, i) => i >= _stats.Count || line != _stats[i]).Select(line => $"stat {line}")];
    }

    private static List<string> Stats(Copy copy)
    {
        using var scope = Guards.Enter();
        return SetupSheets.StatsText(copy.Combat);
    }
}
