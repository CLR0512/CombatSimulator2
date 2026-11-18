using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// A search started from a report: its own snapshot and seed instead of the live game's.
internal static partial class SearchStart
{
    /// <summary>
    /// A search of <paramref name="request"/> on <paramref name="snapshot"/>, as <see cref="New"/> makes one; the
    /// loadout is the slot the request names, unchecked against the live game, which isn't the report's character.
    /// </summary>
    public static SearchRun FromSnapshot(
        SearchRequest request,
        double share,
        string where,
        ulong seed,
        Func<int> nextGeneration,
        Snapshot snapshot,
        string? setupHash
    )
    {
        RequireDependencies();
        var game = LiveGame.Require();
        var slot = request.Setup.Loadout == Model.Setup.Current ? null : request.Setup.Loadout;
        return Begin(
            request,
            share,
            where,
            seed,
            nextGeneration,
            snapshot,
            slot,
            setupHash ?? "",
            LiveGame.TargetName(game, TargetSpec.FromKey(request.Target), masked: false),
            LiveGame.Prices(game)
        );
    }
}
