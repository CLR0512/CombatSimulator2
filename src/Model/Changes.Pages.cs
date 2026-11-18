using System.Globalization;

namespace MelvorMods.CombatSimulator2.Model;

// The Pets, Firemaking and Agility pages' changes: pet=<pet>:on|off, bonfire=<tier>|out and
// technique=<technique>:<level>. Whether a pet is found, a tier can be reached or a level is within the technique's
// cap is for the copy to say.
public sealed partial record Change
{
    /// <summary>A bonfire put out: no tier burning.</summary>
    public const string Out = "out";

    /// <summary>The level asked for, for a technique change.</summary>
    public int? TechniqueLevel => Kind == ChangeKind.Technique ? int.Parse(Value, CultureInfo.InvariantCulture) : null;

    /// <summary>The tier asked for, for a bonfire change that lights one.</summary>
    public string? Tier => Kind == ChangeKind.Bonfire && Value != Out ? Value : null;

    private static string? PetProblem(string text, string rest, out string? subject, out string? value)
    {
        if (!SubjectAndValue(rest, out subject, out value))
            return $"'{text}': write pet=<pet>:on or pet=<pet>:off";
        return value is On or Off ? null : $"'{text}': a pet is on or off";
    }

    private static string? BonfireProblem(string text, string rest, out string? value)
    {
        value = rest;
        var parts = rest.Split(':');
        return rest == None || parts.Length > 2 || parts.Any(p => p.Length == 0)
            ? $"'{text}': write bonfire=<tier> or bonfire=out"
            : null;
    }

    private static string? TechniqueProblem(string text, string rest, out string? subject, out string? value) =>
        SubjectAndValue(rest, out subject, out value)
            ? Whole(text, ref value, LowestLevel, HighestLevel, "a technique's level")
            : $"'{text}': write technique=<technique>:<level>";

    // <id>:<value>, the id with or without its namespace.
    private static bool SubjectAndValue(string rest, out string? subject, out string? value)
    {
        var colon = rest.LastIndexOf(':');
        subject = colon > 0 ? rest[..colon] : null;
        value = colon > 0 ? rest[(colon + 1)..] : null;
        var parts = subject?.Split(':') ?? [];
        return value is { Length: > 0 } && parts.Length is 1 or 2 && parts.All(p => p.Length > 0);
    }
}
