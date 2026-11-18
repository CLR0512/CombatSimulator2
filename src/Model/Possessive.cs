namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// English possessives of names put into text: a name ending in s takes a bare apostrophe ("Fells' tasks", not
/// "Fells's tasks"). Messages write <c>{0}'s</c> for any name; this mends the text once the name is in. A
/// translation without <c>'s</c> after the name is left as it is.
/// </summary>
public static class Possessive
{
    /// <summary><paramref name="text"/> with "name's" made "name'" for each string value ending in s.</summary>
    public static string Mend(string text, IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values)
        {
            if (value is string name && name.Length > 0 && name[^1] is 's' or 'S')
                text = text.Replace(name + "'s", name + "'", StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>"Vannah's" or "Fells'".</summary>
    public static string Of(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > 0 && name[^1] is 's' or 'S' ? name + "'" : name + "'s";
    }
}
