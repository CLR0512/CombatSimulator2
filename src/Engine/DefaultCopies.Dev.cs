namespace MelvorMods.CombatSimulator2.Engine;

// What cs2dev host cores=<n> and stock=<n> put in place of the machine's cores and the default stock.
internal static partial class DefaultCopies
{
    /// <summary>The cores the rule reads instead of the machine's; 0 for the machine's own.</summary>
    public static int CoresOverride { get; set; }

    static partial void OverrideCores(ref int cores)
    {
        if (CoresOverride > 0)
            cores = CoresOverride;
    }

    /// <summary>The bare copies the next warm-ups ask for instead of the default; null for the default.</summary>
    public static int? StockOverride { get; set; }

    static partial void OverrideStock(ref int stock) => stock = StockOverride ?? stock;
}
