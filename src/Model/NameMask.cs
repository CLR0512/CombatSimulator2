using System.Globalization;
using System.Text.RegularExpressions;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// Hides names a player typed in text a bug report carries: loadout names become <c>&lt;loadout N&gt;</c> (N the
/// slot), preset names <c>&lt;preset&gt;</c> and kept comparisons' names <c>&lt;name&gt;</c>. A name is replaced
/// only as a whole word and with the case it was typed in; the longest names are tried first, so a name inside
/// another doesn't break it.
/// </summary>
public sealed class NameMask
{
    private readonly Regex? _pattern;
    private readonly Dictionary<string, string> _replacements;

    private NameMask(Dictionary<string, string> replacements)
    {
        _replacements = replacements;
        if (replacements.Count == 0)
            return;
        var names = replacements.Keys.OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal);
        _pattern = new Regex(
            @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", names.Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])",
            RegexOptions.CultureInvariant
        );
    }

    /// <param name="loadouts">Each loadout's slot number and name.</param>
    public static NameMask For(
        IEnumerable<(int Slot, string Name)> loadouts,
        IEnumerable<string> presets,
        IEnumerable<string> comparisons
    )
    {
        ArgumentNullException.ThrowIfNull(loadouts);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(comparisons);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string name, string replacement)
        {
            var trimmed = name.Trim();
            if (trimmed.Length > 0)
                replacements.TryAdd(trimmed, replacement);
        }
        foreach (var (slot, name) in loadouts)
            Add(name, $"<loadout {slot.ToString(CultureInfo.InvariantCulture)}>");
        foreach (var name in presets)
            Add(name, "<preset>");
        foreach (var name in comparisons)
            Add(name, "<name>");
        return new NameMask(replacements);
    }

    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _pattern == null ? text : _pattern.Replace(text, m => _replacements[m.Value]);
    }
}
