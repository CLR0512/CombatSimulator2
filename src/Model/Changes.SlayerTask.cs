namespace MelvorMods.CombatSimulator2.Model;

// Whether single monsters are fought on a slayer task, whatever the save holds: the side's choice (task=on|off) and one
// monster's own (taskfor=<area>/<enemy>:on|off|save). Without either, a monster is fought as the save has it.
public sealed partial record Change
{
    /// <summary>A monster's own choice that puts it back to the save's task, over the side's choice.</summary>
    public const string Save = "save";

    /// <summary>
    /// Whether <paramref name="changes"/> fight the single monster <paramref name="targetKey"/> on a slayer task
    /// (<see cref="On"/>) or off one (<see cref="Off"/>): its own choice, else the side's; null as the save has it.
    /// </summary>
    public static string? TaskChoice(ChangeList? changes, string targetKey)
    {
        if (changes == null)
            return null;
        var choice =
            changes.Find(new ChangeKey(ChangeKind.SlayerTaskFor, targetKey))?.Value
            ?? changes.Find(new ChangeKey(ChangeKind.SlayerTask, null))?.Value;
        return choice == Save ? null : choice;
    }

    private static string? TaskProblem(string text, string rest, out string? value)
    {
        value = rest;
        return rest is On or Off ? null : $"'{text}': write task=on or task=off";
    }

    private static string? TaskForProblem(string text, string rest, out string? subject, out string? value)
    {
        subject = value = null;
        var colon = rest.LastIndexOf(':');
        if (colon <= 0)
            return $"'{text}': write taskfor=<area>/<enemy>:on, :off or :save";
        subject = rest[..colon];
        value = rest[(colon + 1)..];
        var parts = subject.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p.Count(c => c == ':') > 1 || p.EndsWith(':')))
            return $"'{text}': write taskfor=<area>/<enemy>:on, :off or :save";
        return value is On or Off or Save ? null : $"'{text}': a monster's task is on, off or save";
    }
}
