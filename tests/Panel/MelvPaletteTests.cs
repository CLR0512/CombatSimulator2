using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MelvPaletteTests
{
    // Every role CS2 draws, each with only a normal state, except a button with hover and pressed too.
    private const string Button = """
        "button": {
          "normal": { "fill": "panel", "border": null },
          "hover": { "fill": "hover", "border": "edge" },
          "pressed": { "fill": "pressed", "border": "edge" }
        }
        """;

    private static string Minimal(int version = 1, string? dropRole = null)
    {
        var roles = MelvPalette
            .Roles.Where(r => r != dropRole && r != "button")
            .Select(r => $"\"{r}\": {{ \"normal\": {{ \"fill\": \"panel\", \"border\": \"edge\" }} }}")
            .ToList();
        if (dropRole != "button")
            roles.Add(Button);
        return $$"""
            {
              "version": {{version}},
              "colours": {
                "panel": "#0a0b0cff",
                "edge": "#ff000080",
                "hover": "#00ff00ff",
                "pressed": "#0000ffff"
              },
              "roles": {
                {{string.Join(",\n", roles)}}
              },
              "borderWidth": 2,
              "cornerRadius": 3
            }
            """;
    }

    private static MelvPalette Palette(string json)
    {
        var load = MelvPalette.Parse(json);
        Assert.Null(load.Reason);
        return Assert.IsType<MelvPalette>(load.Palette);
    }

    [Fact]
    public void ColoursReadAsRgba()
    {
        var style = Palette(Minimal()).Style("panel", "normal");
        Assert.Equal(new PaletteColour(10 / 255f, 11 / 255f, 12 / 255f, 1), style.Fill);
        Assert.Equal(new PaletteColour(1, 0, 0, 128 / 255f), style.Border);
    }

    [Fact]
    public void NullMeansNone()
    {
        var style = Palette(Minimal()).Style("button", "normal");
        Assert.NotNull(style.Fill);
        Assert.Null(style.Border);
    }

    [Fact]
    public void HoverPressedFallsBackToPressed()
    {
        var palette = Palette(Minimal());
        Assert.Equal(palette.Style("button", "pressed"), palette.Style("button", "hover_pressed"));
        Assert.NotEqual(palette.Style("button", "normal"), palette.Style("button", "hover_pressed"));
    }

    [Fact]
    public void HoverPressedFallsBackToNormalWithoutPressed()
    {
        var palette = Palette(Minimal());
        Assert.Equal(palette.Style("panel", "normal"), palette.Style("panel", "hover_pressed"));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hover")]
    public void OtherMissingStatesFallBackToNormal(string state)
    {
        var palette = Palette(Minimal());
        Assert.Equal(palette.Style("panel", "normal"), palette.Style("panel", state));
    }

    [Fact]
    public void ADefinedStateIsUsed()
    {
        var style = Palette(Minimal()).Style("button", "hover");
        Assert.Equal(new PaletteColour(0, 1, 0, 1), style.Fill);
    }

    [Fact]
    public void NamedColoursRead()
    {
        var palette = Palette(Minimal());
        Assert.Equal(new PaletteColour(0, 1, 0, 1), palette.Colour("hover"));
        Assert.Null(palette.Colour("nope"));
    }

    [Fact]
    public void BorderWidthAndCornerRadiusRead()
    {
        var palette = Palette(Minimal());
        Assert.Equal(2, palette.BorderWidth);
        Assert.Equal(3, palette.CornerRadius);
    }

    [Fact]
    public void Version2IsUnavailable()
    {
        var load = MelvPalette.Parse(Minimal(version: 2));
        Assert.Null(load.Palette);
        Assert.Contains("version 2", load.Reason!.English(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFileIsUnavailable()
    {
        var load = MelvPalette.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "palette.json"));
        Assert.Null(load.Palette);
        Assert.Contains("missing", load.Reason!.English(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnparsableJsonIsUnavailable()
    {
        var load = MelvPalette.Parse("{ \"version\": 1, ");
        Assert.Null(load.Palette);
        Assert.Contains("unreadable", load.Reason!.English(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingRoleIsUnavailable()
    {
        var load = MelvPalette.Parse(Minimal(dropRole: "slot"));
        Assert.Null(load.Palette);
        Assert.Contains("slot", load.Reason!.English(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARoleNamingAnUnknownColourIsUnavailable()
    {
        var load = MelvPalette.Parse(
            Minimal().Replace("\"fill\": \"panel\"", "\"fill\": \"nope\"", StringComparison.Ordinal)
        );
        Assert.Null(load.Palette);
        Assert.Contains("nope", load.Reason!.English(), StringComparison.Ordinal);
    }

    // A palette change in mElvUI that breaks CS2 fails the tests: the fixture is a copy of mElvUI's palette, kept
    // equal to it where both mods are, since mElvUI's own file is not in this mod's folder.
    [Fact]
    public void MElvUIsPaletteHasEveryRoleCs2Draws()
    {
        var load = MelvPalette.Load(TestPaths.Fixtures("mElvUI-palette.json"));
        Assert.Null(load.Reason);
        var palette = load.Palette!;
        foreach (var role in MelvPalette.Roles)
            Assert.NotNull(palette.Style(role, "normal").Fill ?? palette.Style(role, "normal").Border);
    }
}
