using System.Globalization;
using System.Text.RegularExpressions;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Where a stat sits in the game's own stats view.</summary>
public enum StatGroup
{
    Levels,
    Offence,
    Defence,
    Other,
    Totals,
}

/// <summary>
/// One out-of-combat stat as the game computes it. <c>Key</c> is stable across setups (<c>accuracy</c>,
/// <c>evasion:&lt;stance id&gt;</c>, <c>power:&lt;damage type id&gt;</c>, <c>stat:&lt;combat stat id&gt;</c>, …),
/// so two sheets line up. <c>Name</c> is the game's own where it has one, shown in the language of the moment
/// otherwise.
/// <c>Value</c> is null for a stat that is a name (the stance, the element); <c>Text</c> is the value as the game
/// formats it in English.
/// </summary>
public sealed record StatLine(string Key, StatGroup Group, Note Name, double? Value, string Text);

/// <summary>Groups of modifier sources, in the order they are shown.</summary>
public enum ModifierGroup
{
    Equipment,
    SetBonus,
    Prayer,
    Toolbelt,
    Food,
    StanceSpell,
    Pet,
    Shop,
    Mastery,
    Agility,
    Astrology,
    Area,
    Other,
}

/// <summary>Whether a modifier helps, hurts or neither, as the game colours it.</summary>
public enum ModifierSign
{
    Positive,
    Negative,
    Neutral,
}

/// <summary>
/// One modifier value with its source, described as the game describes it (markup removed, its condition added).
/// <c>OnEnemy</c>: it applies to the enemy rather than the player.
/// </summary>
public sealed record ModifierLine(
    ModifierGroup Group,
    string SourceId,
    string SourceName,
    string Description,
    ModifierSign Kind,
    bool OnEnemy
);

/// <summary>A setup's stats and modifiers, read from a prepared copy of it.</summary>
public sealed record SetupSheet(
    Setup Setup,
    string? SetupHash,
    string GameBuild,
    IReadOnlyList<StatLine> Stats,
    IReadOnlyList<ModifierLine> Modifiers,
    DateTime ReadAt
);

/// <summary>A stat on both sides: either may be missing; <c>Delta</c> is B − A when both are numbers.</summary>
public sealed record StatDiff(string Key, StatLine? A, StatLine? B, double? Delta)
{
    /// <summary>Whether the sides differ: one is missing, the numbers differ, or (for a name) the text does.</summary>
    public bool Differs =>
        A == null || B == null || (Delta is { } d ? d != 0 : !string.Equals(A.Text, B.Text, StringComparison.Ordinal));

    /// <summary>
    /// Whether B is better than A: more is better, except for an interval (the attack interval, the equipment's turn
    /// interval), where shorter is. Null when the numbers are equal or either is not a number.
    /// </summary>
    public bool? BBetter =>
        Delta is { } d && d != 0
            ? (Key.EndsWith("interval", StringComparison.OrdinalIgnoreCase) ? d < 0 : d > 0)
            : null;
}

/// <summary>A modifier line and the sides that have it.</summary>
public sealed record ModifierSide(ModifierLine Line, bool InA, bool InB)
{
    public bool OneSide => InA != InB;
}

/// <summary>
/// Two sheets side by side: stats by key, and modifier lines marked with the side or sides that have them.
/// </summary>
public sealed partial record SheetDiff(IReadOnlyList<StatDiff> Stats, IReadOnlyList<ModifierSide> Modifiers)
{
    /// <summary>
    /// The two sheets side by side: A's stats in A's order then B's own; A's modifier lines then B's own. A modifier
    /// line matches one on the other side with the same group, source and description, one for one.
    /// </summary>
    public static SheetDiff Of(SetupSheet a, SetupSheet b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var bStats = b.Stats.GroupBy(s => s.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
        var stats = new List<StatDiff>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stat in a.Stats)
        {
            if (!seen.Add(stat.Key))
                continue;
            var other = bStats.GetValueOrDefault(stat.Key);
            stats.Add(new StatDiff(stat.Key, stat, other, other?.Value - stat.Value));
        }
        foreach (var stat in b.Stats.Where(s => seen.Add(s.Key)))
            stats.Add(new StatDiff(stat.Key, null, stat, null));

        var unmatched = b.Modifiers.GroupBy(ModifierKey).ToDictionary(g => g.Key, g => g.Count());
        var modifiers = new List<ModifierSide>();
        foreach (var line in a.Modifiers)
        {
            var key = ModifierKey(line);
            var inB = unmatched.TryGetValue(key, out var left) && left > 0;
            if (inB)
                unmatched[key] = left - 1;
            modifiers.Add(new ModifierSide(line, true, inB));
        }
        foreach (var line in b.Modifiers)
        {
            var key = ModifierKey(line);
            if (unmatched.TryGetValue(key, out var left) && left > 0)
            {
                // Lines A didn't match, in B's order.
                unmatched[key] = left - 1;
                modifiers.Add(new ModifierSide(line, false, true));
            }
        }
        return new SheetDiff(stats, modifiers);
    }

    private static (ModifierGroup, string, string) ModifierKey(ModifierLine line) =>
        (line.Group, line.SourceId, line.Description);

    /// <summary>
    /// B − A written the way the two values are written ("2.7s", "4s" → "+1.3s"), when both are one number with
    /// the same text around it; "" when they are equal; null when they don't line up (names, other units). A stat's
    /// value can be held in other units than it is shown (milliseconds shown as seconds), so the shown text is
    /// what the difference follows.
    /// </summary>
    public static string? TextDelta(string a, string b)
    {
        if (Split(a) is not { } x || Split(b) is not { } y || x.Prefix != y.Prefix || x.Suffix != y.Suffix)
            return null;
        var decimals = Math.Max(x.Decimals, y.Decimals);
        var delta = Math.Round(y.Value - x.Value, decimals);
        if (delta == 0)
            return "";
        var number = Math.Abs(delta)
            .ToString("#,0." + new string('0', decimals), CultureInfo.InvariantCulture)
            .TrimEnd('.');
        return x.Prefix + (delta > 0 ? "+" : "-") + number + x.Suffix;
    }

    private static (string Prefix, double Value, int Decimals, string Suffix)? Split(string text)
    {
        var match = Number().Match(text);
        if (!match.Success)
            return null;
        var digits = match.Groups["num"].Value;
        var dot = digits.IndexOf('.', StringComparison.Ordinal);
        return double.TryParse(
            digits.Replace(",", "", StringComparison.Ordinal),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? (match.Groups["pre"].Value, value, dot < 0 ? 0 : digits.Length - dot - 1, match.Groups["suf"].Value)
            : null;
    }

    [GeneratedRegex(@"^(?<pre>[^0-9+\-.]*)(?<num>[+\-]?\d[\d,]*(\.\d+)?)(?<suf>[^0-9]*)$")]
    private static partial Regex Number();
}

/// <summary>The modifier search: part of the source's name or of the description, in any case.</summary>
public static class ModifierSearch
{
    public static bool Matches(ModifierLine line, string? text)
    {
        ArgumentNullException.ThrowIfNull(line);
        var wanted = text?.Trim();
        return string.IsNullOrEmpty(wanted)
            || line.SourceName.Contains(wanted, StringComparison.CurrentCultureIgnoreCase)
            || line.Description.Contains(wanted, StringComparison.CurrentCultureIgnoreCase);
    }
}
