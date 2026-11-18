using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class LanguageListTests
{
    // The locales `mm locale pull` may write (LOCALES in tools/mm/crowdin.py), which the tests can't read: kept equal
    // to that list by hand.
    private static readonly string[] Accepted = """
        ar bg bn ca cs da de el eo es et eu fa fi fil fr ga gl he hi hr hu hy id is it ja ka kk ko lt lv mk ms nb nl
        pl pt_BR pt_PT ro ru sk sl sq sr sv sw ta te th tr uk ur uz vi zh_CN zh_HK zh_TW
        """.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void EveryAcceptedLocaleHasItsOwnName()
    {
        foreach (var code in Accepted)
            Assert.NotEqual(code, LanguageList.NativeName(code));
        Assert.Equal(
            [.. Accepted.Append(LanguageList.English).Order(StringComparer.Ordinal)],
            LanguageList.Named.Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void EnglishComesFirstAndIsNeverPartial()
    {
        var list = LanguageList.Of([("fr", 0.62), ("en", 0.1), ("de", 1)]);
        Assert.Equal(["en", "de", "fr"], list.Select(l => l.Code));
        Assert.False(list[0].Partial);
        Assert.Equal("English", list[0].Label);
        Assert.Equal(["en"], LanguageList.Of([]).Select(l => l.Code));
    }

    [Fact]
    public void APartialLanguageShowsItsShareRoundedDown()
    {
        var list = LanguageList.Of([("fr", 0.629), ("de", 1), ("es", 0.999)]);
        Assert.Equal("Deutsch", list.Single(l => l.Code == "de").Label);
        Assert.Equal("Français · 62%", list.Single(l => l.Code == "fr").Label);
        Assert.Equal("Español · 99%", list.Single(l => l.Code == "es").Label);
    }

    [Fact]
    public void AChoiceCs2DoesNotShipShowsTheGamesLanguage()
    {
        Assert.Null(LanguageList.Applied(null, ["fr"]));
        Assert.Equal("fr", LanguageList.Applied("fr", ["fr"]));
        Assert.Null(LanguageList.Applied("de", ["fr"]));
        Assert.Equal("en", LanguageList.Applied("en", []));
    }
}
