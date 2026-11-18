namespace MelvorMods.CombatSimulator2.Model;

/// <summary>What moves in the panel; each kind has one length and one easing wherever it is used.</summary>
public enum MotionKind
{
    /// <summary>A view, tab or panel appearing, and a floating word appearing and going.</summary>
    Fade,

    /// <summary>A bar or a chart column reaching its new length.</summary>
    Fill,

    /// <summary>A number counting to its new value.</summary>
    Count,

    /// <summary>The target strip opening and closing.</summary>
    Strip,

    /// <summary>The band sliding along a running bar, again and again.</summary>
    Sheen,

    /// <summary>The ring growing from the status dot, again and again.</summary>
    Pulse,

    /// <summary>What is being worked out again fading and returning, again and again.</summary>
    Breathe,
}

/// <summary>
/// How long each motion takes at each level, in seconds: 0 is at once, and for what repeats 0 is standing still.
/// </summary>
public static class MotionTimes
{
    /// <summary>The longest a motion that ends may take at Full.</summary>
    public const double Longest = 0.15;

    /// <summary>The longest fade kept at Reduced.</summary>
    public const double LongestReduced = 0.08;

    public static IReadOnlyList<MotionKind> Kinds { get; } = Enum.GetValues<MotionKind>();

    /// <summary>Whether the kind repeats for as long as its state lasts, rather than ending.</summary>
    public static bool Repeats(MotionKind kind) => kind is MotionKind.Sheen or MotionKind.Pulse or MotionKind.Breathe;

    public static double Seconds(MotionKind kind, MotionLevel level) =>
        level switch
        {
            MotionLevel.Full => kind switch
            {
                MotionKind.Fade => 0.10,
                MotionKind.Fill => 0.15,
                MotionKind.Count => 0.15,
                MotionKind.Strip => 0.10,
                MotionKind.Sheen => 3.2,
                MotionKind.Pulse => 1.0,
                MotionKind.Breathe => 1.4,
                _ => 0,
            },
            MotionLevel.Reduced => kind == MotionKind.Fade ? LongestReduced : 0,
            _ => 0,
        };

    /// <summary>The length the kind had before the levels; null for one that didn't move then.</summary>
    public static double? Before(MotionKind kind) =>
        kind switch
        {
            MotionKind.Fade => 0.14,
            MotionKind.Fill => 0.35,
            MotionKind.Count => 0.40,
            MotionKind.Sheen => 1.2,
            MotionKind.Pulse => 1.0,
            MotionKind.Breathe => 1.4,
            _ => null,
        };

    /// <summary>The level's name in commands and <c>cs2 ui</c>.</summary>
    public static string Name(MotionLevel level) =>
        level switch
        {
            MotionLevel.Full => "full",
            MotionLevel.Reduced => "reduced",
            _ => "off",
        };

    public static bool TryParse(string name, out MotionLevel level)
    {
        foreach (var each in Enum.GetValues<MotionLevel>())
            if (Name(each) == name)
            {
                level = each;
                return true;
            }
        level = MotionLevel.Full;
        return false;
    }
}
