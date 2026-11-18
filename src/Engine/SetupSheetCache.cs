using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// The stats and modifiers of setups read so far, so the same setup asked about again is answered at once while
/// the live setup is as it was. Main thread only.
/// </summary>
internal sealed class SetupSheetCache
{
    private readonly Dictionary<string, SetupSheet> _sheets = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the stats and modifiers of each of <paramref name="sides"/> from a check copy, as a run would fight
    /// with it; <paramref name="done"/> runs on the main thread with the sheets in the same order, or the error.
    /// </summary>
    public void Read(IReadOnlyList<Setup> sides, Action<IReadOnlyList<SetupSheet>?, Exception?> done)
    {
        ArgumentNullException.ThrowIfNull(sides);
        ArgumentNullException.ThrowIfNull(done);
        MainThread.Assert("RunService.ReadSetup");
        var game = LiveGame.Require();
        if (!Dependencies.IsAvailable(Dependencies.SetupSheet))
            throw new CommandException("stats and modifiers are unsupported on this game build (see cs2 deps)");
        if (sides.Any(s => s.Changes != null) && !Dependencies.IsAvailable(Dependencies.Changes))
            throw new CommandException("changes are unsupported on this game build (see cs2 deps)");
        var slots = sides.Select(s => RunService.CheckLoadout(s, () => LiveGame.Loadouts(game))).ToList();
        var hash = Fingerprints.SetupHash(game);
        var keys = sides.Select(s => $"{game.CharacterName}|{s.Identity}|{hash}").ToList();
        if (keys.All(_sheets.ContainsKey))
        {
            done([.. keys.Select(k => _sheets[k])], null);
            return;
        }
        ChangeCheck.ReadSheets(
            game,
            [.. sides.Select((s, i) => (s, slots[i]))],
            hash,
            (sheets, error) =>
            {
                if (sheets != null)
                {
                    for (var i = 0; i < sheets.Count; i++)
                        _sheets[keys[i]] = sheets[i];
                }
                done(sheets, error);
            }
        );
    }

    /// <summary>Forgets the sheets read so far (their text is in the language they were read in).</summary>
    public void Clear() => _sheets.Clear();
}
