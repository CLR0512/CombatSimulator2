namespace MelvorMods.CombatSimulator2.Model;

// A master's blocks as a change: blocks=<master>:<enemy>[,<enemy>] or blocks=<master>:none. The game gives a master
// two block slots; which enemies a master can block, and whether a slot is open, is for the copy to say.
public sealed partial record Change
{
    // Kinds whose text isn't <kind>=<subject>:<value> with plain ids: a master's lists, a monster's target key, and the
    // pages' own values (a pet on or off, a bonfire's tier or out, a technique's level).
    private static bool HasOwnForm(ChangeKind kind) =>
        kind
            is ChangeKind.Blocks
                or ChangeKind.Loadouts
                or ChangeKind.SlayerTask
                or ChangeKind.SlayerTaskFor
                or ChangeKind.Pet
                or ChangeKind.Bonfire
                or ChangeKind.Technique;

    private static string? OwnProblem(ChangeKind kind, string text, string rest, out string? subject, out string? value)
    {
        subject = null;
        value = null;
        return kind switch
        {
            ChangeKind.Blocks => BlocksProblem(text, rest, out subject, out value),
            ChangeKind.Loadouts => LoadoutsProblem(text, rest, out subject, out value),
            ChangeKind.SlayerTask => TaskProblem(text, rest, out value),
            ChangeKind.SlayerTaskFor => TaskForProblem(text, rest, out subject, out value),
            ChangeKind.Pet => PetProblem(text, rest, out subject, out value),
            ChangeKind.Bonfire => BonfireProblem(text, rest, out value),
            _ => TechniqueProblem(text, rest, out subject, out value),
        };
    }

    private string? OwnShortForm() =>
        Kind switch
        {
            ChangeKind.Blocks => BlocksShortForm(),
            ChangeKind.Loadouts => LoadoutsShortForm(),
            ChangeKind.SlayerTaskFor => "taskfor=" + Ids.BriefKey(Subject!) + ":" + Value,
            _ => null,
        };

    // The value in its one normal form for the kinds that have one; null for the rest.
    private static string? NormalValue(ChangeKind kind, string value) =>
        kind switch
        {
            ChangeKind.Blocks => NormalBlocks(value),
            ChangeKind.Loadouts => NormalLoadouts(value),
            ChangeKind.Bonfire => value == Out ? Out : Ids.Full(value),
            _ => null,
        };

    private static string FullSubject(ChangeKind kind, string subject) =>
        kind == ChangeKind.SlayerTaskFor ? Ids.FullKey(subject) : Ids.Full(subject);

    /// <summary>The most enemies one master blocks: it has two slots.</summary>
    public const int MostBlocks = 2;

    /// <summary>The enemies blocked, for a blocks change; empty for none and for other kinds.</summary>
    public IReadOnlyList<string> Blocked => Kind == ChangeKind.Blocks && Value != None ? Value.Split(',') : [];

    // The enemies in full, each once, in id order: equal blocks have one short form whatever order they were given in.
    private static string NormalBlocks(string value) =>
        value == None
            ? None
            : string.Join(',', value.Split(',').Select(Ids.Full).Distinct().Order(StringComparer.Ordinal));

    // Short ids when every id is in the base namespace; any other namespace keeps every id in full, so a namespace
    // can't be read as the master's id.
    private string BlocksShortForm()
    {
        var full = new[] { Subject! }
            .Concat(Blocked)
            .Any(id => !id.StartsWith(Ids.Base + ":", StringComparison.Ordinal));
        string Id(string id) => full ? id : Ids.Brief(id);
        var value = Value == None ? None : string.Join(',', Blocked.Select(Id));
        return "blocks=" + Id(Subject!) + ":" + value;
    }

    private static string? BlocksProblem(string text, string rest, out string? subject, out string? value)
    {
        subject = value = null;
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == rest.Length - 1)
            return $"'{text}': write blocks=<master>:<enemy>[,<enemy>] or blocks=<master>:none";
        // The master has a namespace when it is the base one, or when its enemies then read as ids.
        var masterEnds = colon;
        var after = rest[(colon + 1)..];
        if (
            rest[..colon] == Ids.Base
            || after.Split(',').Any(e => e.Count(c => c == ':') > 1)
            || after.EndsWith(":" + None, StringComparison.Ordinal)
        )
            masterEnds = rest.IndexOf(':', colon + 1);
        if (masterEnds <= 0 || masterEnds == rest.Length - 1)
            return $"'{text}': write blocks=<master>:<enemy>[,<enemy>] or blocks=<master>:none";
        subject = rest[..masterEnds];
        value = rest[(masterEnds + 1)..];
        if (value == None)
            return null;
        var enemies = value.Split(',');
        if (enemies.Any(e => e.Length == 0 || e == None))
            return $"'{text}': name each enemy once, or none alone";
        if (enemies.Select(Ids.Full).Distinct().Count() > MostBlocks)
            return $"'{text}': a master blocks at most {MostBlocks} enemies";
        return null;
    }
}
