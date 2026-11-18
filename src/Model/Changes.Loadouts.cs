namespace MelvorMods.CombatSimulator2.Model;

/// <summary>One monster of a master and the saved loadout it is fought with.</summary>
public readonly record struct MonsterLoadout(string Enemy, string Slot);

// The saved loadout each of a master's monsters is fought with, as a change:
// loadouts=<master>:<enemy>@<slot>[,<enemy>@<slot>] or loadouts=<master>:none. A monster not named is fought with the
// side's setup. Whether a slot exists and is unlocked, and whether an enemy is in the master's pool, is for the copy
// to say.
public sealed partial record Change
{
    /// <summary>The monsters given a loadout, by enemy id, for a loadouts change; empty for none and other kinds.</summary>
    public IReadOnlyList<MonsterLoadout> Loadouts =>
        Kind == ChangeKind.Loadouts && Value != None ? [.. Value.Split(',').Select(ReadPair)] : [];

    private static MonsterLoadout ReadPair(string pair)
    {
        var at = pair.IndexOf('@', StringComparison.Ordinal);
        return new(pair[..at], pair[(at + 1)..]);
    }

    // The ids in full, in enemy id order: equal choices have one short form whatever order they were given in.
    private static string NormalLoadouts(string value) =>
        value == None
            ? None
            : string.Join(
                ',',
                value
                    .Split(',')
                    .Select(ReadPair)
                    .Select(p => Ids.Full(p.Enemy) + "@" + Ids.Full(p.Slot))
                    .Order(StringComparer.Ordinal)
            );

    // Short ids when every id is in the base namespace, as blocks are written.
    private string LoadoutsShortForm()
    {
        var full = new[] { Subject! }
            .Concat(Loadouts.SelectMany(p => new[] { p.Enemy, p.Slot }))
            .Any(id => !id.StartsWith(Ids.Base + ":", StringComparison.Ordinal));
        string Id(string id) => full ? id : Ids.Brief(id);
        var value = Value == None ? None : string.Join(',', Loadouts.Select(p => Id(p.Enemy) + "@" + Id(p.Slot)));
        return "loadouts=" + Id(Subject!) + ":" + value;
    }

    private static string? LoadoutsProblem(string text, string rest, out string? subject, out string? value)
    {
        const string form = "write loadouts=<master>:<enemy>@<loadout>[,<enemy>@<loadout>] or loadouts=<master>:none";
        subject = value = null;
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == rest.Length - 1)
            return $"'{text}': {form}";
        // The master has a namespace when it is the base one, or when its monsters then read as ids.
        var masterEnds = colon;
        var after = rest[(colon + 1)..];
        if (
            rest[..colon] == Ids.Base
            || after.Split(',').Any(p => p.Split('@')[0].Count(c => c == ':') > 1)
            || after.EndsWith(":" + None, StringComparison.Ordinal)
        )
            masterEnds = rest.IndexOf(':', colon + 1);
        if (masterEnds <= 0 || masterEnds == rest.Length - 1)
            return $"'{text}': {form}";
        subject = rest[..masterEnds];
        value = rest[(masterEnds + 1)..];
        if (value == None)
            return null;
        var pairs = value.Split(',');
        if (pairs.Any(p => p.Count(c => c == '@') != 1 || p.StartsWith('@') || p.EndsWith('@')))
            return $"'{text}': {form}";
        if (pairs.Select(p => Ids.Full(ReadPair(p).Enemy)).Distinct().Count() < pairs.Length)
            return $"'{text}': give each enemy at most once";
        return null;
    }
}
