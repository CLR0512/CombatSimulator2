using System.Text.Json;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The translators of each language CS2 ships, with the names they chose to show on Crowdin, kept by hand in
/// <c>data/locale/credits.json</c> (<c>{ "fr": ["name", …] }</c>). Nothing shows until a language has names.
/// </summary>
internal static class AboutCredits
{
    public const string File = "credits.json";

    /// <summary>The Translations section, or null when no language has a translator to name.</summary>
    public static VBoxContainer? Make()
    {
        var credits = Read();
        if (credits.Count == 0)
            return null;
        var section = Ui.Column(6, "Cs2AboutCredits");
        // Translators: Heading over the people who translated CS2, one line per language.
        section.AddChild(AboutParts.Heading(Tr.Get("Translations")));
        foreach (var (code, names) in credits.OrderBy(c => c.Key, StringComparer.Ordinal))
            section.AddChild(
                Ui.Label($"{LanguageList.NativeName(code)}: {string.Join(", ", names)}", TextStyle.Small, wrap: true)
            );
        return section;
    }

    // Languages with at least one name; a file that can't be read names nobody, and says why in the log.
    private static Dictionary<string, List<string>> Read()
    {
        if (AboutIcons.Folder is not { } folder)
            return [];
        var path = Path.Combine(folder, "data", "locale", File);
        if (!System.IO.File.Exists(path))
            return [];
        try
        {
            var all =
                JsonSerializer.Deserialize<Dictionary<string, List<string>>>(System.IO.File.ReadAllText(path)) ?? [];
            return all.Where(c => c.Value is { Count: > 0 }).ToDictionary(c => c.Key, c => c.Value);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"translation credits not read from {File}", e);
            return [];
        }
    }
}
