namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A combat place as the guard reads it: whether the live character can enter it, and who is fought there.</summary>
public sealed record SpoilerPlace(string Id, string Name, bool Open, IReadOnlyList<string> Enemies);

/// <summary>An enemy as the guard reads it: whether the live character has killed it.</summary>
public sealed record SpoilerEnemy(string Id, string Name, bool Killed);

/// <summary>A pet as the guard reads it: whether the live character has found it.</summary>
public sealed record SpoilerPet(string Id, bool Found);

/// <summary>
/// What Spoiler guard hides: places the live character can't enter and has never fought in, monsters it has
/// neither killed nor can meet in a place it can enter, and pets it hasn't found, so nothing the game already shows is
/// hidden. Hidden things are named <see cref="Mask"/>, as the game's completion log names them.
/// </summary>
public sealed class Spoilers
{
    public const string Mask = "???";

    public static readonly Spoilers None = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal)
    );

    private static volatile Spoilers _current = None;

    /// <summary>
    /// What is hidden now; <see cref="None"/> while the guard is off. Read from any thread, replaced on the main thread.
    /// </summary>
    public static Spoilers Current
    {
        get => _current;
        set => _current = value ?? None;
    }

    private readonly HashSet<string> _ids;
    private readonly HashSet<string> _names;
    private readonly HashSet<string> _pets;

    private Spoilers(HashSet<string> ids, HashSet<string> names, HashSet<string> pets)
    {
        _ids = ids;
        _names = names;
        _pets = pets;
    }

    /// <summary>How many monsters and places are hidden.</summary>
    public int Count => _ids.Count;

    public static Spoilers Of(
        IEnumerable<SpoilerPlace> places,
        IEnumerable<SpoilerEnemy> enemies,
        IEnumerable<SpoilerPet>? pets = null
    )
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(enemies);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hiddenNames = new HashSet<string>(StringComparer.Ordinal);
        var shownNames = new HashSet<string>(StringComparer.Ordinal);
        var met = new HashSet<string>(StringComparer.Ordinal);
        var enemyList = enemies.ToList();
        var killed = enemyList.Where(e => e.Killed).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var place in places)
        {
            // A place the character has fought in stays known, as a finished quest's is after the game stops showing it.
            if (place.Open || place.Enemies.Any(killed.Contains))
            {
                met.UnionWith(place.Enemies);
                shownNames.Add(place.Name);
                continue;
            }
            ids.Add(place.Id);
            hiddenNames.Add(place.Name);
        }
        foreach (var enemy in enemyList)
        {
            if (enemy.Killed || met.Contains(enemy.Id))
            {
                shownNames.Add(enemy.Name);
                continue;
            }
            ids.Add(enemy.Id);
            hiddenNames.Add(enemy.Name);
        }
        // A name something shown also has stays readable: masking it would hide what the game shows.
        hiddenNames.ExceptWith(shownNames);
        hiddenNames.Remove("");
        var unfound = (pets ?? []).Where(p => !p.Found).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        return new Spoilers(ids, hiddenNames, unfound);
    }

    /// <summary>Whether both hide the same things under the same names.</summary>
    public bool SameAs(Spoilers other) =>
        ReferenceEquals(this, other)
        || (
            other != null
            && _ids.SetEquals(other._ids)
            && _names.SetEquals(other._names)
            && _pets.SetEquals(other._pets)
        );

    /// <summary>Whether the pet with this id is hidden: the live character hasn't found it.</summary>
    public bool PetHidden(string id) => _pets.Contains(id);

    /// <summary>Whether the enemy or place with this id is hidden.</summary>
    public bool Hides(string id) => _ids.Contains(id);

    /// <summary>
    /// A name as shown: <see cref="Mask"/> for a hidden enemy's or place's name, and each hidden part of
    /// "enemy (place)" masked on its own; anything else unchanged.
    /// </summary>
    public string Name(string name)
    {
        if (_names.Count == 0 || string.IsNullOrEmpty(name))
            return name;
        if (_names.Contains(name))
            return Mask;
        var open = name.IndexOf(" (", StringComparison.Ordinal);
        if (open <= 0 || !name.EndsWith(')'))
            return name;
        var enemy = name[..open];
        var place = name[(open + 2)..^1];
        if (!_names.Contains(enemy) && !_names.Contains(place))
            return name;
        return $"{Name(enemy)} ({Name(place)})";
    }

    /// <summary>A note's value as shown: a name masked by <see cref="Name"/>, anything else unchanged.</summary>
    public object Value(object value) => value is string text ? Name(text) : value;
}
