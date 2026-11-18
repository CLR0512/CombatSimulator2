using System.Globalization;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>Invariant number formatting for replies.</summary>
internal static class Fmt
{
    public static string N(double v) =>
        double.IsFinite(v)
            ? Math.Abs(v) >= 100
                ? v.ToString("#,0", CultureInfo.InvariantCulture)
                : Math.Abs(v) >= 1
                    ? v.ToString("0.##", CultureInfo.InvariantCulture)
                    : v.ToString("0.####", CultureInfo.InvariantCulture)
            : v > 0
                ? "∞"
                : "-∞";

    public static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Ms(double ms) => ms.ToString("0", CultureInfo.InvariantCulture);

    public static string Range(Estimate e) => $"{N(e.Point)} [{N(e.Low)}, {N(e.High)}]";
}
