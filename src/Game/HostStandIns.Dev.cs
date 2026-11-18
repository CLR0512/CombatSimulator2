namespace MelvorMods.CombatSimulator2.Game;

// cs2dev host standins=off: one host without stand-ins, to tell their cost on a cold start from the collector's.
internal static partial class HostStandIns
{
    /// <summary>Set in a host's environment, it loads textures and sounds as the game does.</summary>
    public const string OffVariable = "MELVORMODS_CS2_STANDINS_OFF";

    private static readonly bool _off = Environment.GetEnvironmentVariable(OffVariable) is { Length: > 0 };

    static partial void SwitchedOff(ref bool off) => off = _off;
}
