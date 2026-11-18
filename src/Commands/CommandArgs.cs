using System.Globalization;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// A subcommand's arguments: positional words, then <c>key=value</c> options. Flags (<c>full</c>,
/// <c>ignore-req</c>) are words the subcommand declares, taken out of the positional list.
/// </summary>
internal sealed class CommandArgs
{
    private CommandArgs() { }

    public List<string> Words { get; } = [];

    /// <summary>
    /// Every argument in the order given, options included (an option given twice keeps only its last value).
    /// </summary>
    public List<string> Raw { get; } = [];
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static CommandArgs Parse(
        IEnumerable<string> args,
        IReadOnlyCollection<string> options,
        IReadOnlyCollection<string> flags
    )
    {
        var parsed = new CommandArgs();
        foreach (var arg in args)
        {
            parsed.Raw.Add(arg);
            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                var key = arg[..eq];
                if (!options.Contains(key, StringComparer.OrdinalIgnoreCase))
                    throw new CommandException($"unknown option '{key}'");
                parsed.Options[key] = arg[(eq + 1)..];
            }
            else if (flags.Contains(arg, StringComparer.OrdinalIgnoreCase))
                parsed.Flags.Add(arg);
            else
                parsed.Words.Add(arg);
        }
        return parsed;
    }

    public string? Word(int i) => i < Words.Count ? Words[i] : null;

    public bool Flag(string name) => Flags.Contains(name);

    public int Int(string key, int fallback, int min, int max)
    {
        if (!Options.TryGetValue(key, out var text))
            return fallback;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max
            ? v
            : throw new CommandException($"{key} must be a whole number from {min} to {max}");
    }

    public ulong? ULong(string key)
    {
        if (!Options.TryGetValue(key, out var text))
            return null;
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new CommandException($"{key} must be a whole number from 0 to {ulong.MaxValue}");
    }

    public double? Double(string key, double min, double max)
    {
        if (!Options.TryGetValue(key, out var text))
            return null;
        return
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max
            ? v
            : throw new CommandException(
                string.Create(CultureInfo.InvariantCulture, $"{key} must be a number from {min} to {max}")
            );
    }

    public string? String(string key) => Options.TryGetValue(key, out var v) ? v : null;

    public TimeUnit Unit(string key, TimeUnit fallback)
    {
        if (!Options.TryGetValue(key, out var text))
            return fallback;
        return TimeUnits.TryParse(text, out var unit)
            ? unit
            : throw new CommandException($"{key} must be kill, second, minute, hour or day");
    }
}
