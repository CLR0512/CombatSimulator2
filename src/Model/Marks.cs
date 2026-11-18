namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// Invisible marks in a text for the parts the panel draws in another colour: a side's letter in gold, a change for
/// the better in green or for the worse in red, a secondary line in grey. Text that leaves the panel (a command's
/// reply, copied text) is given through <see cref="Plain"/>.
/// </summary>
public static class Marks
{
    /// <summary>Before a side's letter.</summary>
    public const char Side = '\u0001';

    /// <summary>Opens a coloured part; the character after it says which colour.</summary>
    public const char Open = '\u0002';

    /// <summary>Closes a coloured part.</summary>
    public const char Close = '\u0003';

    public const char GoodCode = 'g';
    public const char BadCode = 'b';
    public const char MutedCode = 'm';

    public static string Good(string text) => Wrap(GoodCode, text);

    public static string Bad(string text) => Wrap(BadCode, text);

    public static string Muted(string text) => Wrap(MutedCode, text);

    private static string Wrap(char code, string text) => text.Length == 0 ? text : $"{Open}{code}{text}{Close}";

    /// <summary>Whether <paramref name="text"/> carries any mark.</summary>
    public static bool In(string text) => text.AsSpan().IndexOfAny(Side, Open) >= 0;

    /// <summary><paramref name="text"/> without its marks.</summary>
    public static string Plain(string text)
    {
        if (!In(text))
            return text;
        var plain = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == Open)
                i++;
            else if (c != Side && c != Close)
                plain.Append(c);
        }
        return plain.ToString();
    }
}
