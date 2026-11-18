using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Every setup page of a side, read from the game it is given (a check copy after the side's changes). Each page's
/// groups, entries, names, limits, requirements and modifier texts come from the game's registries and the copy's
/// components: nothing here lists a constellation, a purchase or a quest by hand.
/// </summary>
internal static class PageReader
{
    /// <summary>
    /// What each changeable entry holds now, by the key a change to it would have. Read before a side's changes, it
    /// is what <see cref="Read"/> shows as the character's own value.
    /// </summary>
    public static Dictionary<ChangeKey, int> Values(MelvorGame game)
    {
        var values = new Dictionary<ChangeKey, int>();
        AstrologyPage.Values(game, values);
        GuildPage.Values(game, values);
        ShopPage.Values(game, values);
        QuestsPage.Values(game, values);
        HerblorePage.Values(game, values);
        PetsPage.Values(game, values);
        FiremakingPage.Values(game, values);
        AgilityPage.Values(game, values);
        return values;
    }

    /// <summary>The pages in the order the page list shows them. <paramref name="save"/>: see <see cref="Values"/>.</summary>
    public static List<SetupPage> Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save) =>
        [
            .. SetupPage.All.Select(page =>
                page.Id switch
                {
                    SetupPage.Astrology => AstrologyPage.Read(game, save),
                    SetupPage.Guild => GuildPage.Read(game, save),
                    SetupPage.Shop => ShopPage.Read(game, save),
                    SetupPage.Quests => QuestsPage.Read(game, save),
                    SetupPage.Herblore => HerblorePage.Read(game, save),
                    SetupPage.Pets => PetsPage.Read(game, save),
                    SetupPage.Firemaking => FiremakingPage.Read(game, save),
                    SetupPage.Agility => AgilityPage.Read(game, save),
                    SetupPage.Slayer => SlayerPage.Read(game),
                    SetupPage.Gear => Page(SetupPage.Gear, PageState.Changeable),
                    _ => Page(page.Id, PageState.NoData, Note.Of("Not in this build")),
                }
            ),
        ];

    /// <summary>A page without entries of its own: one drawn from the setup itself, or one that has nothing.</summary>
    public static SetupPage Page(string id, PageState state, Note? reason = null) =>
        new(id, SetupPage.All.First(p => p.Id == id).Section, state, reason, [], []);

    /// <summary>A page whose game calls this build lacks: nothing on it can be read or changed.</summary>
    public static SetupPage Unsupported(string id) =>
        Page(id, PageState.NoData, Note.Of("unsupported on this game build"));

    /// <summary>The entry's value before the side's changes: what the character has.</summary>
    public static int Saved(IReadOnlyDictionary<ChangeKey, int> save, ChangeKey key, int now) =>
        save.TryGetValue(key, out var value) ? value : now;
}
