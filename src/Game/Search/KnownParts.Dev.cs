namespace MelvorMods.CombatSimulator2.Game.Search;

internal static partial class KnownParts
{
    private static volatile string? _unknown;

    /// <summary>Makes one item read as carrying an unknown part until told otherwise; null for none.</summary>
    public static void MakeUnknown(string? item) => _unknown = item;

    static partial void Unknown(string item, ref bool unknown) => unknown = item == _unknown;
}
