using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// The checks of sides made so far, so the same side asked about again is answered at once while the live setup is
/// as it was. Main thread only.
/// </summary>
internal sealed class SideChecks
{
    // Enough for the edits of one sitting; each holds a sheet's text.
    private const int Kept = 64;

    private readonly Dictionary<string, SideCheck> _checks = new(StringComparer.Ordinal);

    /// <summary>
    /// Checks <paramref name="changes"/> on <paramref name="side"/>'s base (its loadout slot given as
    /// <paramref name="loadoutSlot"/>) in a check copy; <paramref name="done"/> runs on the main thread with the check
    /// or the error.
    /// </summary>
    public void Check(Setup side, string? loadoutSlot, ChangeList changes, Action<SideCheck?, Exception?> done)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(done);
        var game = LiveGame.Require();
        if (!Dependencies.IsAvailable(Dependencies.Changes))
            throw new CommandException("changes are unsupported on this game build (see cs2 deps)");
        if (!Dependencies.IsAvailable(Dependencies.SetupSheet))
            throw new CommandException("stats and modifiers are unsupported on this game build (see cs2 deps)");
        var hash = Fingerprints.SetupHash(game);
        var key = string.Join(
            '|',
            game.CharacterName,
            side.Loadout,
            side.IgnoreRequirements ? "ignore-req" : "",
            changes.ShortForm,
            hash
        );
        if (_checks.TryGetValue(key, out var known))
        {
            done(known, null);
            return;
        }
        ChangeCheck.Side(
            game,
            side,
            loadoutSlot,
            changes,
            hash,
            (check, error) =>
            {
                if (check != null)
                {
                    if (_checks.Count >= Kept)
                        _checks.Clear();
                    _checks[key] = check;
                }
                done(check, error);
            }
        );
    }

    /// <summary>Forgets the checks (their text is in the language they were made in).</summary>
    public void Clear() => _checks.Clear();
}
