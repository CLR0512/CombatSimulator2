using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// The latest answer per kind of search and target, kept on disk beside the runs. An answer is shown again while
/// what it was searched under still holds; when the setup, the gear, the levels, a build or the margin has changed
/// since, it is shown as out of date with what changed.
/// </summary>
internal static partial class KeptAnswers
{
    /// <summary>
    /// The build an answer is checked against: a new one puts it out of date. Debug builds count as one, or every
    /// deploy while working would put every answer out of date.
    /// </summary>
    public static string Build
    {
        get
        {
            var build = RunService.Cs2Mvid;
            DebugBuild(ref build);
            return build;
        }
    }

    static partial void DebugBuild(ref string build);

    /// <summary>Set by the mod when it loads; without it nothing is kept.</summary>
    public static AnswerStore? Store { get; set; }

    /// <summary>Keeps a search's answer when the search ran to its end; a cancelled or failed one changes nothing.</summary>
    public static void Keep(Answer answer)
    {
        if (Store is not { } store)
            return;
        store.WriteFailed ??= e => Log.Warn("the search's answers couldn't be written", e);
        store.KeepLater(answer);
    }

    /// <summary>
    /// The kept answer for <paramref name="target"/> (the last one found when null) with what has changed since it
    /// was searched, read against <paramref name="setup"/> as it stands now: the catalogue is read from a check copy
    /// off the main thread, and <paramref name="done"/> runs on it. Null when no answer is kept.
    /// </summary>
    public static void Find(
        SearchKind kind,
        Setup setup,
        string? target,
        double share,
        Action<(Answer Answer, IReadOnlyList<Note> Changed)?> done
    )
    {
        MainThread.Assert("KeptAnswers.Find");
        var game = LiveGame.Require();
        var snapshot = LiveGame.TakeSnapshot(game);
        var character = snapshot.Character;
        var kept =
            target == null
                ? Store?.Latest(kind, character)
                : Store?.Find(kind, target, new AnswerKey { Character = character })?.Answer;
        if (kept == null)
        {
            done(null);
            return;
        }
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        setup.Character = character;
        SearchStart.ReadOnce(
            new SearchRequest(kind, setup, kept.Target, kept.Key.Goal, 1, 1)
            {
                Kinds = [.. kept.Key.Kinds.Select(n => ChoiceKinds.TryParse(n, out var k) ? k : default)],
                IncludeUnowned = kept.Key.IncludeUnowned,
            },
            PlanWhat.Catalogue,
            snapshot,
            slot,
            (reply, error) =>
            {
                var catalogue = reply?.Catalogue;
                if (catalogue == null)
                {
                    Log.Warn(
                        "the kept answer couldn't be checked against the setup",
                        error ?? new InvalidOperationException("no catalogue")
                    );
                    done((kept, [Note.Of("could not be checked")]));
                    return;
                }
                // What the answer was asked for stays as asked; what it was searched under is read now.
                var now = kept.Key with
                {
                    Setup = setup.Identity,
                    Gear = catalogue.GearHash,
                    Levels = catalogue.LevelsHash,
                    GameBuild = snapshot.GameBuild,
                    ModBuild = Build,
                    Share = share,
                    Seed = null,
                    Supplies = reply!.Supplies ?? kept.Key.Supplies,
                };
                done((kept, kept.Key.Changed(now)));
            }
        );
    }
}
