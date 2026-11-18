using GamesByMalcs.Melvor2.Entities.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Slayer: the page holds the choices CS2 makes itself (single monsters on or off task, every master on its best
/// setup), so it lists nothing from the character. The save's own tasks and blocks don't reach a run: a master is
/// fought over its whole pool, and its blocks are set on the master's Monsters tab.
/// </summary>
internal static class SlayerPage
{
    public static SetupPage Read(MelvorGame game)
    {
        var slayer = game.Entities.GetAllObjects().OfType<SlayerEntity>().FirstOrDefault();
        return slayer == null || slayer.Tasks.ActiveMasters.Count == 0
            ? PageReader.Page(SetupPage.Slayer, PageState.NoData, Note.Of("Not in this build"))
            : new SetupPage(SetupPage.Slayer, PageSection.Skills, PageState.Changeable, null, [], []);
    }
}
