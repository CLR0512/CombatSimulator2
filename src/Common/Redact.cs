using System.Text.RegularExpressions;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>The names a report hides: the user profile's path, the user name and the machine name.</summary>
internal sealed record RedactionNames(string? Profile, string? User, string? Machine)
{
    public static RedactionNames Current() =>
        new(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName,
            Environment.MachineName
        );
}

/// <summary>Hides who and where a report comes from, in every text it carries.</summary>
internal static class Redact
{
    public const string User = "<user>";
    public const string Machine = "<machine>";

    // Shorter names are replaced only where they are a whole path segment: as words they would hit ordinary text.
    private const int WordLength = 3;

    /// <summary>
    /// Replaces the profile path (with either slash), then the user name and the machine name, ignoring case: as a
    /// path segment always, and as a whole word when at least 3 characters long.
    /// </summary>
    public static string Text(string text, RedactionNames names)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(names);
        if (names.Profile is { Length: > 3 } profile)
        {
            foreach (var form in (string[])[profile, profile.Replace('\\', '/'), profile.Replace('/', '\\')])
                text = text.Replace(form, User, StringComparison.OrdinalIgnoreCase);
        }
        text = Name(text, names.User, User);
        return Name(text, names.Machine, Machine);
    }

    private static string Name(string text, string? name, string replacement)
    {
        if (string.IsNullOrWhiteSpace(name))
            return text;
        var quoted = Regex.Escape(name);
        // Never inside a placeholder already written: a user called "user" would turn "<user>" into "<<user>>".
        var pattern =
            name.Length >= WordLength
                ? $@"(?<![\p{{L}}\p{{N}}_<]){quoted}(?![\p{{L}}\p{{N}}_>])"
                : $@"(?<=^|[\\/]){quoted}(?=$|[\\/])";
        return Regex.Replace(
            text,
            pattern,
            replacement,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline,
            TimeSpan.FromSeconds(5)
        );
    }
}
