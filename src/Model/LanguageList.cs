using System.Globalization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A language CS2's text can be shown in, by its own name, with the share of CS2's text translated.</summary>
public sealed record LanguageChoice(string Code, string Name, double Share)
{
    /// <summary>Some of CS2's text is still in English.</summary>
    public bool Partial => Share < 1;

    /// <summary>The name, with its share when partial ("Français · 62%"), rounded down so 99.9% never reads 100%.</summary>
    public string Label =>
        Partial ? string.Create(CultureInfo.InvariantCulture, $"{Name} · {Math.Floor(Share * 100):0}%") : Name;
}

/// <summary>The languages a player can pick for CS2's own text: English, then each one CS2 ships a translation for.</summary>
public static class LanguageList
{
    /// <summary>The language CS2 is written in: always complete.</summary>
    public const string English = "en";

    // Each language by its own name, for every locale the translation tooling accepts. Fixed here rather than read
    // from .NET, whose globalisation the game may run without.
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["ar"] = "العربية",
        ["bg"] = "Български",
        ["bn"] = "বাংলা",
        ["ca"] = "Català",
        ["cs"] = "Čeština",
        ["da"] = "Dansk",
        ["de"] = "Deutsch",
        ["el"] = "Ελληνικά",
        ["en"] = "English",
        ["eo"] = "Esperanto",
        ["es"] = "Español",
        ["et"] = "Eesti",
        ["eu"] = "Euskara",
        ["fa"] = "فارسی",
        ["fi"] = "Suomi",
        ["fil"] = "Filipino",
        ["fr"] = "Français",
        ["ga"] = "Gaeilge",
        ["gl"] = "Galego",
        ["he"] = "עברית",
        ["hi"] = "हिन्दी",
        ["hr"] = "Hrvatski",
        ["hu"] = "Magyar",
        ["hy"] = "Հայերեն",
        ["id"] = "Bahasa Indonesia",
        ["is"] = "Íslenska",
        ["it"] = "Italiano",
        ["ja"] = "日本語",
        ["ka"] = "ქართული",
        ["kk"] = "Қазақ тілі",
        ["ko"] = "한국어",
        ["lt"] = "Lietuvių",
        ["lv"] = "Latviešu",
        ["mk"] = "Македонски",
        ["ms"] = "Bahasa Melayu",
        ["nb"] = "Norsk bokmål",
        ["nl"] = "Nederlands",
        ["pl"] = "Polski",
        ["pt_BR"] = "Português (Brasil)",
        ["pt_PT"] = "Português (Portugal)",
        ["ro"] = "Română",
        ["ru"] = "Русский",
        ["sk"] = "Slovenčina",
        ["sl"] = "Slovenščina",
        ["sq"] = "Shqip",
        ["sr"] = "Српски",
        ["sv"] = "Svenska",
        ["sw"] = "Kiswahili",
        ["ta"] = "தமிழ்",
        ["te"] = "తెలుగు",
        ["th"] = "ไทย",
        ["tr"] = "Türkçe",
        ["uk"] = "Українська",
        ["ur"] = "اردو",
        ["uz"] = "Oʻzbekcha",
        ["vi"] = "Tiếng Việt",
        ["zh_CN"] = "简体中文",
        ["zh_HK"] = "繁體中文（香港）",
        ["zh_TW"] = "繁體中文（台灣）",
    };

    /// <summary>Every locale with a name.</summary>
    public static IReadOnlyCollection<string> Named => Names.Keys;

    /// <summary>A language's own name; the code itself for one the table lacks.</summary>
    public static string NativeName(string code) => Names.GetValueOrDefault(code, code);

    /// <summary>English, then each shipped language in code order, with its share of CS2's text.</summary>
    public static IReadOnlyList<LanguageChoice> Of(IEnumerable<(string Locale, double Share)> shipped) =>
        [
            new(English, NativeName(English), 1),
            .. shipped
                .Where(s => s.Locale != English)
                .OrderBy(s => s.Locale, StringComparer.Ordinal)
                .Select(s => new LanguageChoice(s.Locale, NativeName(s.Locale), Math.Clamp(s.Share, 0, 1))),
        ];

    /// <summary>
    /// The language to show for the player's <paramref name="chosen"/> one: null (the game's) when they chose none, or
    /// one CS2 doesn't ship.
    /// </summary>
    public static string? Applied(string? chosen, IReadOnlyCollection<string> shipped) =>
        chosen != null && (chosen == English || shipped.Contains(chosen)) ? chosen : null;
}
