using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// Best has a colour of its own: far enough from every deaths colour, the plain bar and the selection that a best
/// row is told apart at a glance. The colours are read from the panel's source, which needs the game to compile.
/// </summary>
public sealed partial class LookColourTests
{
    private const double Apart = 20;

    [GeneratedRegex("""Color (\w+) = Color\.FromHtml\("(#[0-9a-fA-F]{6})"\)""")]
    private static partial Regex Constant();

    private static Dictionary<string, string> Colours() =>
        Constant()
            .Matches(File.ReadAllText(Path.Combine(TestPaths.ModFolder(), "src", "Panel", "Widgets", "Look.cs")))
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    [Theory]
    [InlineData("Safe")]
    [InlineData("Risky")]
    [InlineData("Deadly")]
    [InlineData("BarFill")]
    public void BestIsToldApartFromEveryOtherMark(string other)
    {
        var colours = Colours();
        var distance = Distance(colours["Best"], colours[other]);
        Assert.True(distance >= Apart, $"Best {colours["Best"]} is {distance:0.0} from {other} {colours[other]}");
    }

    [Fact]
    public void BestIsToldApartFromTheSelectionsWhite()
    {
        Assert.True(Distance(Colours()["Best"], "#ffffff") >= Apart);
    }

    [Fact]
    public void SomeDeathsIsToldApartFromFivePercentDeaths()
    {
        var colours = Colours();
        // Orange leans to red on purpose, so it reads as a deaths colour and not as a gold: it may sit nearer to red
        // than best may, and is still clearly not red.
        Assert.True(Distance(colours["Risky"], colours["Deadly"]) >= 18);
        Assert.True(Distance(colours["Risky"], colours["Safe"]) >= Apart);
    }

    [Theory]
    [InlineData("RanOver", "Best")]
    [InlineData("RanOver", "Safe")]
    [InlineData("RanOver", "Risky")]
    [InlineData("RanOver", "Deadly")]
    [InlineData("RanOver", "BarFill")]
    [InlineData("RanOver", "Running")]
    [InlineData("RanOver", "Engine")]
    [InlineData("ManyRanOver", "Best")]
    [InlineData("ManyRanOver", "Safe")]
    [InlineData("ManyRanOver", "Risky")]
    [InlineData("ManyRanOver", "Deadly")]
    [InlineData("ManyRanOver", "BarFill")]
    [InlineData("ManyRanOver", "Running")]
    [InlineData("ManyRanOver", "Engine")]
    [InlineData("RanOver", "ManyRanOver")]
    public void TheRanOverChipIsToldApartFromEveryOtherMark(string chip, string other)
    {
        var colours = Colours();
        var distance = Distance(colours[chip], colours[other]);
        Assert.True(distance >= Apart, $"{chip} {colours[chip]} is {distance:0.0} from {other} {colours[other]}");
        Assert.True(Distance(colours[chip], "#ffffff") >= Apart);
    }

    // The published CIEDE2000 test pair 1 (Sharma, Wu, Dalal): 2.0425.
    [Fact]
    public void TheDistanceIsCiede2000()
    {
        Assert.Equal(2.0425, Ciede2000((50, 2.6772, -79.7751), (50, 0, -82.7485)), 4);
        Assert.Equal(0, Distance("#e0b84a", "#e0b84a"), 9);
    }

    private static double Distance(string a, string b) => Ciede2000(Lab(a), Lab(b));

    private static (double L, double A, double B) Lab(string hex)
    {
        static double Linear(int c)
        {
            var v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16 / 116.0;
        var r = Linear(int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var g = Linear(int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var b = Linear(int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    private static double Ciede2000((double L, double A, double B) p, (double L, double A, double B) q)
    {
        static double Degrees(double radians) => radians * 180 / Math.PI;
        static double Radians(double degrees) => degrees * Math.PI / 180;
        static double Hue(double a, double b) => a == 0 && b == 0 ? 0 : (Degrees(Math.Atan2(b, a)) + 360) % 360;
        var meanC = (Math.Sqrt(p.A * p.A + p.B * p.B) + Math.Sqrt(q.A * q.A + q.B * q.B)) / 2;
        var g = 0.5 * (1 - Math.Sqrt(Math.Pow(meanC, 7) / (Math.Pow(meanC, 7) + Math.Pow(25, 7))));
        var (a1, a2) = ((1 + g) * p.A, (1 + g) * q.A);
        var (c1, c2) = (Math.Sqrt(a1 * a1 + p.B * p.B), Math.Sqrt(a2 * a2 + q.B * q.B));
        var (h1, h2) = (Hue(a1, p.B), Hue(a2, q.B));
        var dL = q.L - p.L;
        var dC = c2 - c1;
        var dh =
            c1 * c2 == 0 ? 0
            : Math.Abs(h2 - h1) <= 180 ? h2 - h1
            : h2 > h1 ? h2 - h1 - 360
            : h2 - h1 + 360;
        var dH = 2 * Math.Sqrt(c1 * c2) * Math.Sin(Radians(dh / 2));
        var meanL = (p.L + q.L) / 2;
        var meanCp = (c1 + c2) / 2;
        var meanH =
            c1 * c2 == 0 ? h1 + h2
            : Math.Abs(h1 - h2) <= 180 ? (h1 + h2) / 2
            : h1 + h2 < 360 ? (h1 + h2 + 360) / 2
            : (h1 + h2 - 360) / 2;
        var t =
            1
            - 0.17 * Math.Cos(Radians(meanH - 30))
            + 0.24 * Math.Cos(Radians(2 * meanH))
            + 0.32 * Math.Cos(Radians(3 * meanH + 6))
            - 0.20 * Math.Cos(Radians(4 * meanH - 63));
        var rotation = 30 * Math.Exp(-Math.Pow((meanH - 275) / 25, 2));
        var rc = 2 * Math.Sqrt(Math.Pow(meanCp, 7) / (Math.Pow(meanCp, 7) + Math.Pow(25, 7)));
        var sl = 1 + 0.015 * Math.Pow(meanL - 50, 2) / Math.Sqrt(20 + Math.Pow(meanL - 50, 2));
        var sc = 1 + 0.045 * meanCp;
        var sh = 1 + 0.015 * meanCp * t;
        var rt = -Math.Sin(Radians(2 * rotation)) * rc;
        return Math.Sqrt(
            Math.Pow(dL / sl, 2) + Math.Pow(dC / sc, 2) + Math.Pow(dH / sh, 2) + rt * (dC / sc) * (dH / sh)
        );
    }
}
