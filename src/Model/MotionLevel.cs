using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>How much the panel animates.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MotionLevel>))]
public enum MotionLevel
{
    Full,

    /// <summary>Short fades only; fills and counts show at once and nothing loops.</summary>
    Reduced,
    Off,
}
