using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Reads what Spoiler guard hides from the live character: never a setup's copy, whose changes move what can be
/// fought but not what the player has seen.
/// </summary>
internal static class SpoilerGuard
{
    /// <summary>Whether the guard is on: the player's setting.</summary>
    public static Func<bool> IsOn { get; set; } = () => true;

    /// <summary>
    /// Sets <see cref="Spoilers.Current"/> from the live game, or to nothing hidden while the guard is off. True when
    /// what is hidden changed, so views built with the old names are rebuilt.
    /// </summary>
    public static bool Refresh(MelvorGame? game)
    {
        MainThread.Assert("SpoilerGuard.Refresh");
        var before = Spoilers.Current;
        Spoilers.Current =
            IsOn() && game != null && Dependencies.IsAvailable(Dependencies.SpoilerGuard) ? Read(game) : Spoilers.None;
        return !Spoilers.Current.SameAs(before);
    }

    private static Spoilers Read(MelvorGame game)
    {
        var places = game
            .Data.GetDataRegistryOutput<BaseCombatArea>()
            .GetAllObjects()
            .Select(area => new SpoilerPlace(
                area.Id,
                area.DisplayName.Text,
                (area.UnlockRequirements?.AreMet ?? true) && (area.DisplayRequirements?.AreMet ?? true),
                Fought(area)
            ));
        var killed = LiveGame.Combat(game).Statistics.EnemyKilled;
        var enemies = game
            .Data.GetDataRegistryOutput<Enemy>()
            .GetAllObjects()
            .Select(enemy => new SpoilerEnemy(enemy.Id, enemy.DisplayName.Text, killed.GetValue(enemy) > 0));
        return Spoilers.Of(places, enemies, Pets(game), Holders(game));
    }

    // Chests and the like, by the fights that give them, so one only a hidden monster drops is hidden with it.
    private static IEnumerable<SpoilerHolder> Holders(MelvorGame game)
    {
        var sources = Search.ItemSources.All(game);
        foreach (var item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
            if (item.Capabilities.TryGet<OpenableCapability>(out _) && sources.TryGetValue(item, out var from))
                yield return new SpoilerHolder(
                    LiveGame.NameOf(game, item.Id, masked: false),
                    [.. from.Where(s => s.Fight != null).Select(s => s.Fight!)],
                    from.Any(s => s.Fight == null)
                );
    }

    // The live character's pets, found or not; none when the build's pet calls moved.
    private static IEnumerable<SpoilerPet> Pets(MelvorGame game) =>
        Dependencies.IsAvailable(Dependencies.Pets)
            ? Pages.PetsPage.All(game).Select(p => new SpoilerPet(p.Pet.Id, p.Active?.IsUnlocked == true))
            : [];

    private static IReadOnlyList<string> Fought(BaseCombatArea area) =>
        area switch
        {
            CombatArea combat => [.. combat.Enemies.Select(e => e.Id)],
            Dungeon dungeon => [.. dungeon.Enemies.SelectMany(stage => stage.Enemies.Items).Select(w => w.Enemy.Id)],
            _ => [],
        };
}
